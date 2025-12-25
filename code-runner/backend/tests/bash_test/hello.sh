#!/bin/bash

# Simple bash test script
echo "Hello from Bash!"
echo "Current working directory: $(pwd)"
echo "Bash version: $BASH_VERSION"
echo "System info: $(uname -a)"

# Basic math
a=10
b=20
echo "Math test: $a + $b = $((a + b))"

# Array test
fruits=("apple" "banana" "orange")
echo "Array test: ${fruits[@]}"

# Function test
greet() {
    echo "Function test: Hello, $1!"
}

greet "World"

# JSON processing with jq
echo '{"name": "test", "value": 42}' | jq '.'

# Environment variables
echo "PATH: $PATH"
echo "HOME: $HOME"
echo "USER: $USER" | tee /tmp/output.txt