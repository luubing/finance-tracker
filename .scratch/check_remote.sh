#!/bin/bash
echo "--- swagger.json status ---"
curl -s -o /tmp/sw.json -w "HTTP %{http_code}\n" --max-time 10 http://localhost:5270/swagger/v1/swagger.json
echo "--- auth paths ---"
grep -o '"/api/[A-Za-z/]*"' /tmp/sw.json | sort -u | head -20
echo "--- root status ---"
curl -s -o /dev/null -w "HTTP %{http_code}\n" --max-time 10 http://localhost:5270/
