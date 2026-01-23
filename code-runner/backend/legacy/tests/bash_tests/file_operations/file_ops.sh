#!/bin/bash
# File Operations Test

echo "=== File Operations Test ==="

# Create test file
echo "Hello World" > /tmp/test.txt
echo "Line 2" >> /tmp/test.txt
echo "Line 3" >> /tmp/test.txt

echo ""
echo "--- Read file content ---"
cat /tmp/test.txt

echo ""
echo "--- Read specific line ---"
sed -n '2p' /tmp/test.txt

echo ""
echo "--- Word count ---"
wc -w /tmp/test.txt

echo ""
echo "--- Search pattern ---"
grep "Line" /tmp/test.txt

echo ""
echo "--- Replace text ---"
sed 's/Line/Modified/' /tmp/test.txt

echo ""
echo "--- Count lines ---"
wc -l /tmp/test.txt

echo ""
echo "--- File info ---"
ls -la /tmp/test.txt

# Cleanup
rm -f /tmp/test.txt

echo ""
echo "=== File operations completed ==="
