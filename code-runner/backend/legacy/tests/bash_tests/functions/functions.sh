#!/bin/bash
# Functions Test

echo "=== Functions Test ==="

# Define functions
greet() {
    echo "Hello, $1!"
}

add_numbers() {
    local sum=$(($1 + $2))
    echo "$sum"
}

get_date() {
    date +%Y-%m-%d
}

check_file() {
    if [ -f "$1" ]; then
        echo "$1 exists"
        return 0
    else
        echo "$1 does not exist"
        return 1
    fi
}

calculate_factorial() {
    local n=$1
    local result=1
    local i=1
    while [ $i -le $n ]; do
        result=$((result * i))
        i=$((i + 1))
    done
    echo "$result"
}

# Call functions
echo ""
echo "--- Simple function call ---"
greet "World"

echo ""
echo "--- Function with return value ---"
sum=$(add_numbers 5 7)
echo "5 + 7 = $sum"

echo ""
echo "--- Function returning command output ---"
today=$(get_date)
echo "Today's date: $today"

echo ""
echo "--- Function with conditional return ---"
check_file "/etc/hostname"
check_file "/nonexistent"

echo ""
echo "--- Recursive-style loop (factorial) ---"
factorial=$(calculate_factorial 5)
echo "5! = $factorial"

echo ""
echo "--- Function with multiple statements ---"
process_data() {
    local data=$1
    echo "Processing: $data"
    local upper=$(echo "$data" | tr '[:lower:]' '[:upper:]')
    echo "Uppercase: $upper"
    echo "Length: ${#data}"
}

process_data "hello world"

echo ""
echo "=== Functions completed ==="
