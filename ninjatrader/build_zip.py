"""Builds the NinjaTrader 8 import package: dist/TheStratSuite_NT8_v<version>.zip.

The zip holds Info.xml and the two source files under Indicators/TheStratSuite/, the layout
NinjaTrader's Tools > Import > NinjaScript Add-On expects for a source export. Run from any
directory: python3 ninjatrader/build_zip.py
"""

import os
import re
import zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, "src", "Indicators", "TheStratSuite")
FILES = ["TheStratSuite.cs", "TheStratSuiteEngine.cs"]

INFO = """<?xml version="1.0" encoding="utf-8"?>
<NinjaTrader>
  <Export>
    <Version>8.1.2.1</Version>
  </Export>
</NinjaTrader>
"""


def main():
    ind = open(os.path.join(SRC, "TheStratSuite.cs"), encoding="utf-8").read()
    version = re.search(r'public const string Version = "([^"]+)"', ind).group(1)
    os.makedirs(os.path.join(HERE, "dist"), exist_ok=True)
    out = os.path.join(HERE, "dist", f"TheStratSuite_NT8_v{version}.zip")
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
        # Fixed timestamps keep the zip byte-identical between builds of the same source.
        def add(name, data):
            info = zipfile.ZipInfo(name, date_time=(2026, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            z.writestr(info, data)

        add("Info.xml", INFO)
        for f in FILES:
            add(f"Indicators/TheStratSuite/{f}", open(os.path.join(SRC, f), "rb").read())
    print(out)


if __name__ == "__main__":
    main()
