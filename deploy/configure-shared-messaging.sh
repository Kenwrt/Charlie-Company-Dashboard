#!/usr/bin/env bash
set -euo pipefail

gateway_env=/home/ken/wrightapps/wright-messaging/shared/wright-messaging.env
charlie_env=/opt/wrightapps/charlie-company/shared/charlie-company.env

read_setting() {
  local file="$1" key="$2"
  sed -n "s/^${key}=//p" "$file" | tail -n 1
}

if grep -q '^MessagingGateway__Applications__3__Id=' "$gateway_env"; then
  echo "Charlie Company is already registered with the messaging gateway."
  exit 0
fi

umask 077
application_key="$(openssl rand -hex 32)"
application_hash="$(printf '%s' "$application_key" | sha256sum | awk '{print $1}')"
messaging_service_sid="$(read_setting "$gateway_env" 'MessagingGateway__Applications__2__MessagingServiceSid')"
inbound_number="$(read_setting "$gateway_env" 'MessagingGateway__Applications__2__InboundNumber')"

test -n "$messaging_service_sid"
test -n "$inbound_number"
cp "$gateway_env" "${gateway_env}.before-charlie-company"
cp "$charlie_env" "${charlie_env}.before-messaging"

{
  printf '\nMessagingGateway__Applications__3__Id=charlie-company\n'
  printf 'MessagingGateway__Applications__3__DisplayName=Charlie Company\n'
  printf 'MessagingGateway__Applications__3__MessagingServiceSid=%s\n' "$messaging_service_sid"
  printf 'MessagingGateway__Applications__3__InboundNumber=%s\n' "$inbound_number"
  printf 'MessagingGateway__Applications__3__ApiKeySha256=%s\n' "$application_hash"
} >> "$gateway_env"

{
  printf '\nMessaging__BaseUrl=https://messaging.healthcareautomation.services/\n'
  printf 'Messaging__ApplicationId=charlie-company\n'
  printf 'Messaging__ApplicationKey=%s\n' "$application_key"
} >> "$charlie_env"

docker rm -f wright-messaging-api >/dev/null
docker run -d --name wright-messaging-api --restart unless-stopped \
  --env-file "$gateway_env" \
  -p 10.168.168.7:5108:8080 \
  wright-messaging-api:20260826.1 >/dev/null

echo "Charlie Company messaging registration configured without displaying secrets."
