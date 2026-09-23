#!/bin/sh
set -eu

cp /etc/nginx/bolt/http.conf /etc/nginx/conf.d/default.conf
nginx
trap 'exit 0' TERM INT QUIT
trap 'nginx -s quit' EXIT

while [ ! -s /etc/letsencrypt/live/bolt-analyser/fullchain.pem ] || [ ! -s /etc/letsencrypt/live/bolt-analyser/privkey.pem ]; do
    sleep 5 & wait $!
done

cp /etc/nginx/bolt/https.conf /etc/nginx/conf.d/default.conf
nginx -s reload

while :; do
    sleep 3600 & wait $!
    nginx -s reload
done
