#!/bin/bash

echo "=== Environment Variables Test ==="
echo "List all environment variables:"
env | sort

echo -e "\n=== Important paths ==="
echo "PATH: $PATH"
echo "HOME: $HOME"
echo "PWD: $PWD"
echo "USER: $USER"

echo -e "\n=== Test customization variables ==="
echo "CUSTOM_MESSAGE: ${CUSTOM_MESSAGE:-'(not set)'}"
echo "TEST_MODE: ${TEST_MODE:-'(not set)'}"

echo -e "\n=== From config file ==="
if [ -f "config/env.conf" ]; then
    source config/env.conf
    echo "CONFIG_VALUE: ${CONFIG_VALUE:-'(not set)'}"
    echo "APP_NAME: ${APP_NAME:-'(not set)'}"
fi

echo -e "\n=== Execution complete ==="