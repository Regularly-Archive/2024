#!/bin/bash
# Conditionals Test

echo "=== Conditionals Test ==="

# Test variables
VALUE=10
TEXT="hello"
FILE="/etc/hostname"

echo ""
echo "--- Numeric comparison ---"
if [ "$VALUE" -gt 5 ]; then
    echo "$VALUE is greater than 5"
fi

if [ "$VALUE" -eq 10 ]; then
    echo "$VALUE equals 10"
fi

if [ "$VALUE" -lt 20 ]; then
    echo "$VALUE is less than 20"
fi

echo ""
echo "--- String comparison ---"
if [ "$TEXT" = "hello" ]; then
    echo "TEXT equals 'hello'"
fi

if [ "$TEXT" != "world" ]; then
    echo "TEXT does not equal 'world'"
fi

if [ -z "$EMPTY" ]; then
    echo "EMPTY is empty"
fi

echo ""
echo "--- File tests ---"
if [ -f "$FILE" ]; then
    echo "$FILE exists and is a regular file"
fi

if [ -d "/tmp" ]; then
    echo "/tmp is a directory"
fi

if [ -r "$FILE" ]; then
    echo "$FILE is readable"
fi

echo ""
echo "--- If-else chain ---"
SCORE=75
if [ "$SCORE" -ge 90 ]; then
    echo "Grade: A"
elif [ "$SCORE" -ge 80 ]; then
    echo "Grade: B"
elif [ "$SCORE" -ge 70 ]; then
    echo "Grade: C"
else
    echo "Grade: F"
fi

echo ""
echo "--- Case statement ---"
FRUIT="apple"
case "$FRUIT" in
    apple)
        echo "It's an apple"
        ;;
    banana|orange)
        echo "It's banana or orange"
        ;;
    *)
        echo "Unknown fruit"
        ;;
esac

echo ""
echo "--- Logical operators ---"
A=5
B=10
if [ "$A" -gt 1 ] && [ "$B" -lt 20 ]; then
    echo "Both conditions true: A>1 AND B<20"
fi

if [ "$A" -gt 10 ] || [ "$B" -lt 20 ]; then
    echo "At least one condition true: A>10 OR B<20"
fi

echo ""
echo "=== Conditionals completed ==="
