#!/bin/sh
set -eu

until certbot certonly --webroot --webroot-path /var/www/certbot \
    --cert-name bolt-analyser --domain "$LETSENCRYPT_DOMAIN" \
    --email "$LETSENCRYPT_EMAIL" --agree-tos --non-interactive \
    --keep-until-expiring; do
    sleep 3600
done

while :; do
    sleep 43200
    certbot renew --quiet
done
