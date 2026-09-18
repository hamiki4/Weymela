#!/bin/sh
set -eu

health_path=/tmp/weymela-worker/healthy
test -f "$health_path"

interval=${V3__Worker__IntervalSeconds:-5}
case "$interval" in
    ''|*[!0-9]*) exit 1 ;;
esac
test "$interval" -ge 2
test "$interval" -le 60

freshness=$((interval * 4))
if test "$freshness" -lt 60; then
    freshness=60
fi

now=$(date +%s)
updated=$(stat -c %Y "$health_path")
age=$((now - updated))
test "$age" -ge 0
test "$age" -le "$freshness"
