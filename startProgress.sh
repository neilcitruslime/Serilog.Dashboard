#!/bin/bash

docker run --name postgres-dev \
  -e POSTGRES_PASSWORD=devpass \
  -e POSTGRES_USER=devuser \
  -e POSTGRES_DB=logs \
  -p 5432:5432 \
  -d postgres


