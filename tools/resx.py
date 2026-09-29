"""Merge keys into a neutral (Polish) resx and its English counterpart.

usage: python resx.py <base path without .resx> <json file with {"Key": ["pl", "en"], ...}>
"""
import json
import os
import sys
import xml.etree.ElementTree as ET

HEADER = """<?xml version="1.0" encoding="utf-8"?>
<root>
  <resheader name="resmimetype"><value>text/microsoft-resx</value></resheader>
  <resheader name="version"><value>2.0</value></resheader>
  <resheader name="reader"><value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>
  <resheader name="writer"><value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>
</root>
"""


def load(path):
    if not os.path.exists(path):
        return {}
    tree = ET.parse(path)
    return {d.get("name"): d.find("value").text or "" for d in tree.getroot().findall("data")}


def esc(s):
    return s.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")


def save(path, entries):
    lines = HEADER.rstrip().splitlines()[:-1]
    for k in sorted(entries):
        lines.append(f'  <data name="{k}" xml:space="preserve"><value>{esc(entries[k])}</value></data>')
    lines.append("</root>")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines) + "\n")


base, src = sys.argv[1], sys.argv[2]
with open(src, encoding="utf-8") as f:
    new = json.load(f)
pl, en = load(base + ".resx"), load(base + ".en.resx")
for k, (p, e) in new.items():
    pl[k], en[k] = p, e
save(base + ".resx", pl)
save(base + ".en.resx", en)
print(f"{base}: {len(pl)} keys")
