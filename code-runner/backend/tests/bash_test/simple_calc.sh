#!/bin/bash

# Simple calculator script
echo "=== Bash Calculator ==="
echo "Performing basic arithmetic operations:"

# Define numbers
num1=15
num2=7

echo "Numbers: num1=$num1, num2=$num2"
echo "Addition: $num1 + $num2 = $((num1 + num2))"
echo "Subtraction: $num1 - $num2 = $((num1 - num2))"
echo "Multiplication: $num1 * $num2 = $((num1 * num2))"
echo "Division: $num1 / $num2 = $((num1 / num2))"
echo "Modulus: $num1 % $num2 = $((num1 % num2))"

# Power operation
result=$((num1**2))
echo "Power: $num1^2 = $result"

# Working with files
echo "Creating a temporary file..."
temp_file=$(mktemp)
echo "Temporary file: $temp_file"
echo "This is a test" > $temp_file
echo "File contents:"
cat $temp_file
rm $temp_file

echo "=== Calculator complete ==="