# Third-Party Notices

## NetWasm and browser qualification tools

The optional `netwasm0.1` target uses NetWasm SDK 0.10.2 and its 0.10.0
ported-library packages. NetWasm's compiler/tooling uses the NetWasm Community
License; its CoreLib/runtime/generated support uses MIT with upstream notices,
and the Compact/NetWasmGC collectors use Apache-2.0. Review the upstream terms
before commercial use and preserve applicable notices when redistributing
published runtime files:

- https://github.com/zion-sati/NetWasm/blob/main/docs/licensing.md
- https://github.com/zion-sati/NetWasm/blob/main/THIRD-PARTY-NOTICES.md
- https://github.com/zion-sati/NetWasm.Libraries

Playwright is used only for browser regression checks and is licensed under
Apache-2.0. Its downloaded browsers have their own upstream licenses:
https://github.com/microsoft/playwright/blob/main/LICENSE.

## Unihan data

This repository bundles `data/Unihan_Readings.txt`, the Unicode 18.0.0 Unihan readings
data file. Its original header is preserved.

Official release archive: https://www.unicode.org/Public/18.0.0/ucd/Unihan.zip

UAX #38 identifies the Japanese `kJapanese` readings as data from the Moji Joho
Kiban database, owned by CITPC and used under license. Review that property's
terms before extracting or redistributing it separately from the official UCD.

UNICODE LICENSE V3

COPYRIGHT AND PERMISSION NOTICE

Copyright © 1991-2026 Unicode, Inc.

NOTICE TO USER: Carefully read the following legal agreement. BY
DOWNLOADING, INSTALLING, COPYING OR OTHERWISE USING DATA FILES, AND/OR
SOFTWARE, YOU UNEQUIVOCALLY ACCEPT, AND AGREE TO BE BOUND BY, ALL OF THE
TERMS AND CONDITIONS OF THIS AGREEMENT. IF YOU DO NOT AGREE, DO NOT
DOWNLOAD, INSTALL, COPY, DISTRIBUTE OR USE THE DATA FILES OR SOFTWARE.

Permission is hereby granted, free of charge, to any person obtaining a
copy of data files and any associated documentation (the "Data Files") or
software and any associated documentation (the "Software") to deal in the
Data Files or Software without restriction, including without limitation
the rights to use, copy, modify, merge, publish, distribute, and/or sell
copies of the Data Files or Software, and to permit persons to whom the
Data Files or Software are furnished to do so, provided that either (a)
this copyright and permission notice appear with all copies of the Data
Files or Software, or (b) this copyright and permission notice appear in
associated Documentation.

THE DATA FILES AND SOFTWARE ARE PROVIDED "AS IS", WITHOUT WARRANTY OF ANY
KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT OF
THIRD PARTY RIGHTS.

IN NO EVENT SHALL THE COPYRIGHT HOLDER OR HOLDERS INCLUDED IN THIS NOTICE
BE LIABLE FOR ANY CLAIM, OR ANY SPECIAL INDIRECT OR CONSEQUENTIAL DAMAGES,
OR ANY DAMAGES WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS,
WHETHER IN AN ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION,
ARISING OUT OF OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THE DATA
FILES OR SOFTWARE.

Except as contained in this notice, the name of a copyright holder shall
not be used in advertising or otherwise to promote the sale, use or other
dealings in these Data Files or Software without prior written
authorization of the copyright holder.

SPDX-License-Identifier: Unicode-3.0
