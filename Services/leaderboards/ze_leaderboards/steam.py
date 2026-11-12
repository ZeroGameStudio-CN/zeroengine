"""Fixed-purpose Steam publisher transport. Never expose a generic proxy."""
from __future__ import annotations

import json
import logging
import re
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

log = logging.getLogger("zeroengine.leaderboards")


from .common import (AuthenticationRejected as InvalidTicket,
                     ProviderUnavailable as SteamUnavailable, WriteNotSent as SteamWriteNotSent)


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


class SteamPublisher:
    def __init__(self, key_path: str, *, app_id: int, ticket_identity: str, descending_modes=(), logger=None):
        if type(app_id) is not int or not 0 < app_id <= 0xffffffff or not ticket_identity:
            raise ValueError("Explicit Steam application and ticket identity required")
        self.app_id, self.ticket_identity = app_id, ticket_identity
        self.descending_modes = frozenset(descending_modes)
        self.log = logger if logger is not None else log
        key = Path(key_path).read_text(encoding="ascii").strip()
        if not re.fullmatch(r"[0-9a-fA-F]{32}", key):
            raise ValueError("Invalid publisher credential file")
        self._key = key
        # Do not inherit ambient proxies or forward credentials through redirects.
        self._http = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect())

    def _request(self, path: str, params: dict, post: bool = False) -> dict:
        encoded = urllib.parse.urlencode({"appid": self.app_id, **params}).encode("ascii")
        url = "https://partner.steam-api.com" + path
        request = urllib.request.Request(
            url if post else url + "?" + encoded.decode("ascii"),
            data=encoded if post else None,
            headers={"x-webapi-key": self._key, "Content-Type": "application/x-www-form-urlencoded"},
            method="POST" if post else "GET",
        )
        try:
            with self._http.open(request, timeout=10) as response:
                raw = response.read(2 * 1024 * 1024 + 1)
                if len(raw) > 2 * 1024 * 1024:
                    raise SteamUnavailable()
                value = json.loads(raw)
                if not isinstance(value, dict):
                    raise SteamUnavailable()
                return value
        except (OSError, ValueError, urllib.error.HTTPError) as ex:
            # urllib exceptions can contain the authentication-ticket URL.
            raise SteamUnavailable("Steam request unavailable") from None

    def authenticate(self, ticket: str) -> str:
        response = self._request("/ISteamUserAuth/AuthenticateUserTicket/v1/", {
            "ticket": ticket, "identity": self.ticket_identity,
        }).get("response", {})
        result = response.get("params", {})
        steam_id = str(result.get("steamid", ""))
        if (result.get("result") != "OK" or not re.fullmatch(r"[0-9]{17,20}", steam_id) or
                not 0 < int(steam_id) <= 0xFFFFFFFFFFFFFFFF):
            # Steam's numeric reason only; never the ticket, account or response text.
            error = response.get("error")
            code = error.get("errorcode") if isinstance(error, dict) else None
            self.log.warning("leaderboard_ticket_rejected reason=not_ok errorcode=%s", code if type(code) is int else "none")
            raise InvalidTicket()
        if result.get("vacbanned") is True or result.get("publisherbanned") is True:
            self.log.warning("leaderboard_ticket_rejected reason=%s errorcode=none",
                        "vac_banned" if result.get("vacbanned") is True else "publisher_banned")
            raise InvalidTicket()
        return steam_id

    def find_board(self, name: str) -> dict:
        # The GET board list can be cached for an hour. This POST is a lookup
        # only: never create a missing board or supply metadata to change it.
        result = self._request("/ISteamLeaderboards/FindOrCreateLeaderboard/v2/", {
            "name": name, "createifnotfound": "false",
        }, post=True).get("result", {})
        board = result.get("leaderboard")
        if result.get("result") != 1 or not isinstance(board, dict):
            raise SteamUnavailable("Configured leaderboard not found")
        return {"id": board.get("leaderBoardID"), "name": board.get("leaderboardName"),
                "entries": board.get("leaderBoardEntries"), "sortmethod": board.get("leaderBoardSortMethod"),
                "displaytype": board.get("leaderBoardDisplayType"), "onlytrustedwrites": board.get("onlytrustedwrites"),
                "onlyfriendsreads": board.get("onlyfriendsreads")}

    def find_or_create_board(self, name: str, mode: str) -> dict:
        expected_sort = "Descending" if mode in self.descending_modes else "Ascending"
        result = self._request("/ISteamLeaderboards/FindOrCreateLeaderboard/v2/", {
            "name": name, "sortmethod": expected_sort, "displaytype": "Numeric",
            "createifnotfound": "true", "onlytrustedwrites": "true", "onlyfriendsreads": "false",
        }, post=True).get("result", {})
        board = result.get("leaderboard")
        if result.get("result") != 1 or not isinstance(board, dict):
            raise SteamUnavailable("Trusted leaderboard provisioning failed")
        info = {"id": board.get("leaderBoardID"), "name": board.get("leaderboardName"),
                "entries": board.get("leaderBoardEntries"), "sortmethod": board.get("leaderBoardSortMethod"),
                "displaytype": board.get("leaderBoardDisplayType"), "onlytrustedwrites": board.get("onlytrustedwrites"),
                "onlyfriendsreads": board.get("onlyfriendsreads")}
        if (not str(info.get("id", "")).isdigit() or int(info["id"]) <= 0 or info.get("name") != name or
                info.get("onlytrustedwrites") not in (True, 1) or info.get("sortmethod") != expected_sort or
                info.get("displaytype") != "Numeric"):
            raise SteamUnavailable("Provisioned leaderboard metadata mismatch")
        return info

    def verify_board(self, policy: dict) -> None:
        board = self.find_board(policy["targetBoard"])
        # Publisher v2 returns these as strings, unlike the native SDK enums.
        expected_sort = "Descending" if policy["mode"] in self.descending_modes else "Ascending"
        if (str(board.get("id")) != str(policy["boardId"]) or board.get("name") != policy["targetBoard"] or
                board.get("onlytrustedwrites") not in (True, 1) or
                board.get("sortmethod") != expected_sort or board.get("displaytype") != "Numeric"):
            raise SteamUnavailable("Configured leaderboard is not the expected Trusted board")

    def set_score(self, policy: dict, steam_id: str, score: int) -> None:
        try:
            self.verify_board(policy)
        except SteamUnavailable:
            raise SteamWriteNotSent("Steam score request was not sent") from None
        result = self._request("/ISteamLeaderboards/SetLeaderboardScore/v1/", {
            "leaderboardid": policy["boardId"], "steamid": steam_id,
            "score": score, "scoremethod": "KeepBest",
        }, post=True)
        if result.get("result", {}).get("result") != 1:
            # An unexpected response is ambiguous; never silently replay it.
            raise SteamUnavailable("Steam write outcome unconfirmed")
