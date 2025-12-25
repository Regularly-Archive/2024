#!/bin/bash
echo "=== Parameter Test ==="
echo "Script: $0"
echo "Number of args: $#"
echo "Args: $@"

if [ $# -ne 3 ]; then
    echo "Usage: $0 <name> <age> <city>"
    exit 1
fi

echo -e "\n处理参数："
echo "1. Name: $1"
echo "2. Age: $2"
echo "3. City: $3"

echo -e "\n自定义处理："
echo "Hello $1! You are $2 years old from $3."