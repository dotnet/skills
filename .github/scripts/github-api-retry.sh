#!/usr/bin/env bash

# Bounded retry for idempotent, read-only `gh api` calls.
#
# Callers must not use this helper for writes. Permanent client errors fail on
# the first attempt; only 429, 5xx, and common transport failures are retried.
gh_api_read() {
  local max_attempts="${GH_API_READ_MAX_ATTEMPTS:-3}"
  local base_delay="${GH_API_READ_BACKOFF_SECONDS:-2}"
  local max_delay="${GH_API_READ_MAX_BACKOFF_SECONDS:-30}"
  local attempt=1
  local output status error delay retry_after transient
  local error_file

  if ! [[ "$max_attempts" =~ ^[1-9][0-9]*$ ]]; then
    printf 'GH_API_READ_MAX_ATTEMPTS must be a positive integer\n' >&2
    return 2
  fi
  if ! [[ "$base_delay" =~ ^[0-9]+$ ]]; then
    printf 'GH_API_READ_BACKOFF_SECONDS must be a non-negative integer\n' >&2
    return 2
  fi
  if ! [[ "$max_delay" =~ ^[0-9]+$ ]]; then
    printf 'GH_API_READ_MAX_BACKOFF_SECONDS must be a non-negative integer\n' >&2
    return 2
  fi

  error_file=$(mktemp)
  while true; do
    if output=$(gh api "$@" 2>"$error_file"); then
      cat "$error_file" >&2
      rm -f "$error_file"
      printf '%s' "$output"
      return 0
    else
      status=$?
    fi

    error=$(cat "$error_file")
    printf '%s\n' "$error" >&2
    transient=false
    if grep -Eiq \
      'HTTP([[:space:]]+status)?[[:space:]:=]+(429|5[0-9]{2})|status([[:space:]]+code)?[[:space:]:=]+(429|5[0-9]{2})|connection (reset|refused)|timed? out|timeout|temporary failure|could not resolve host|couldn.t resolve host|no such host|error connecting to|failed to connect|TLS handshake|unexpected EOF|remote end hung up|stream error' \
      <<<"$error"; then
      transient=true
    elif grep -Eiq \
      'HTTP([[:space:]]+status)?[[:space:]:=]+403|status([[:space:]]+code)?[[:space:]:=]+403' \
      <<<"$error" &&
      grep -Eiq 'secondary rate limit|abuse detection' <<<"$error"; then
      transient=true
    fi
    if [ "$transient" != "true" ]; then
      rm -f "$error_file"
      return "$status"
    fi
    if [ "$attempt" -ge "$max_attempts" ]; then
      rm -f "$error_file"
      return "$status"
    fi

    delay=$(( base_delay * (1 << (attempt - 1)) ))
    retry_after=$(grep -Eio 'retry[- ]after[[:space:]:=]+[0-9]+' <<<"$error" |
      grep -Eo '[0-9]+' | head -n 1 || true)
    if [ -n "$retry_after" ] && [ "$retry_after" -gt "$delay" ]; then
      delay="$retry_after"
    fi
    if [ "$delay" -gt "$max_delay" ]; then delay="$max_delay"; fi
    printf '[github-api] transient read failure; retrying attempt %s/%s in %ss\n' \
      "$((attempt + 1))" "$max_attempts" "$delay" >&2
    if [ "$delay" -gt 0 ]; then sleep "$delay"; fi
    attempt=$((attempt + 1))
  done
}
