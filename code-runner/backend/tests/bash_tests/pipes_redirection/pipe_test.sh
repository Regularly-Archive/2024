#!/bin/bash
# Pipes and Redirection Test

echo "=== Pipes and Redirection Test ==="

# Create test data
seq 1 10 > /tmp/numbers.txt

echo ""
echo "--- Pipe: sum numbers ---"
seq 1 10 | paste -sd+ | bc

echo ""
echo "--- Pipe: count lines ---"
seq 1 20 | wc -l

echo ""
echo "--- Pipe: filter even numbers ---"
seq 1 10 | grep -E "[0-9]*[02468]$"

echo ""
echo "--- Output to file ---"
echo "Redirected output" > /tmp/output.txt
cat /tmp/output.txt

echo ""
echo "--- Append to file ---"
echo "Appended line" >> /tmp/output.txt
cat /tmp/output.txt

echo ""
echo "--- Stderr redirect ---"
ls /nonexistent 2> /tmp/error.txt || true
cat /tmp/error.txt

echo ""
echo "--- Combined stdout and stderr ---"
echo "stdout" && ls /nonexistent 2>&1 || true

echo ""
echo "--- Here document ---"
cat <<EOF
This is a here document.
Multi-line text without echo.
EOF

echo ""
echo "--- Command substitution ---"
TODAY=$(date +%Y-%m-%d)
echo "Today is: $TODAY"

# Cleanup
rm -f /tmp/numbers.txt /tmp/output.txt /tmp/error.txt

echo ""
echo "=== Pipes and redirection completed ==="
