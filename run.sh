#!/bin/bash
RUN_TESTS=false

for arg in "$@"; do
  if [ "$arg" = "--test" ]; then
    RUN_TESTS=true
  fi
done

docker compose -f docker-compose.yml -f docker-compose.override.yml up -d --build

if [ "$RUN_TESTS" = true ]; then
  dotnet test ToyDb.sln
fi