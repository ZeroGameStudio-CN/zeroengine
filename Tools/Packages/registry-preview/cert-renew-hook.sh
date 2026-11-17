#!/bin/sh
set -eu
[ "${RENEWED_LINEAGE:-}" = "/etc/letsencrypt/live/packages-preview.zerogamestudio.com" ] || exit 0
/www/server/nginx/sbin/nginx -t
/www/server/nginx/sbin/nginx -s reload
