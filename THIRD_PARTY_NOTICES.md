# Third-party notices

## taskbar-battery-widget (MIT)

Parts of this project port the battery reader protocols from https://github.com/Gabrielfch/taskbar-battery-widget
(SteelSeries, DualSense and related device handling).

```
MIT License

Copyright (c) 2026 Gabrielfch

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

The text above was compared with the upstream `LICENSE` file at commit `04a2a342679a13977112eaa66ef1742e52b94cff`
and matches it, apart from blank lines and trailing spaces.

Readers ported from that project, each from the file named, as of the same commit. Each reader sends only fixed
status requests; none writes settings to a device. None has been tested on real hardware unless the README says so.

| Reader here | Upstream file |
|---|---|
| `DualShock4Reader` | `taskbar_battery_widget/sony.py` (DualShock 4 part only) |
