#!/usr/bin/env python3
"""Quick check: what columns does EXU_PRICEELEMENTRATES actually have?
Runs via pyodbc to AXDB50."""
import subprocess, sys

# Use sqlcmd.exe from Windows side (has Kerberos)
query = """
SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'EXU_PRICEELEMENTRATES'
ORDER BY ORDINAL_POSITION;
"""
cmd = ['sqlcmd.exe', '-S', 'xel2012', '-d', 'AXDB50', '-Q', query, '-W', '-s', '|']
result = subprocess.run(cmd, capture_output=True, text=True, timeout=15)
print(result.stdout)
if result.stderr:
    print("STDERR:", result.stderr, file=sys.stderr)
