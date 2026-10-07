#!/bin/sh
# Runs when the container starts (the nginx image runs every script in /docker-entrypoint.d).
# Rewrites the dashboard's appsettings.json from DASHBOARD_API_URL.
set -eu

: "${DASHBOARD_API_URL:?Set DASHBOARD_API_URL to the address the browser reaches the API on, for example http://localhost:5001}"

# The value ends up inside a JSON string, so refuse anything that could break out of it
case "$DASHBOARD_API_URL" in
    *\"*|*\\*) echo "Refusing a setting that contains a quote or backslash" >&2; exit 1 ;;
esac

cat > /usr/share/nginx/html/appsettings.json <<EOF
{
  "ApiUrl": "$DASHBOARD_API_URL"
}
EOF
