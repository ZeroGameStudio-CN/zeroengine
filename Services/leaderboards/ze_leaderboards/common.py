"""Stable run wire helpers; no game, platform or SDK dependency."""
import hashlib
import json

class Rejected(Exception):
    def __init__(self, code, status=400):
        self.code, self.status = code, status

class AuthenticationRejected(Exception):
    pass

class ProviderUnavailable(Exception):
    pass

class WriteNotSent(ProviderUnavailable):
    """Publication definitely did not dispatch; only this failure may retry."""

def canonical(value) -> str:
    return json.dumps(value, sort_keys=True, separators=(",", ":"), allow_nan=False)

def digest(value) -> str:
    return hashlib.sha256(canonical(value).encode()).hexdigest()

def text(value, max_length=127) -> str:
    if not isinstance(value, str) or not 0 < len(value) <= max_length or not value.isascii():
        raise Rejected("invalid_field")
    if any(ord(c) < 32 for c in value):
        raise Rejected("invalid_field")
    return value

def integer(value, minimum=0, maximum=2_147_483_646) -> int:
    if type(value) is not int or not minimum <= value <= maximum:
        raise Rejected("invalid_field")
    return value

def fields(body: dict, expected: set):
    if not isinstance(body, dict) or set(body) != expected:
        raise Rejected("invalid_fields")
