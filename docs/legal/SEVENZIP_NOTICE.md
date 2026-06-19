# 7-Zip notice

Hakamiq CHD Tool can use the 7-Zip command-line tool as an archive extraction backend for ZIP, RAR, and 7Z files.

7-Zip is Copyright (C) Igor Pavlov.

7-Zip is licensed mainly under the GNU LGPL. Some components may use additional BSD-style terms, and the unRAR component has additional restrictions.

Hakamiq CHD Tool does not modify 7-Zip binaries.

If a release bundles 7-Zip, use the official Windows command-line files and keep them under:

```text
Tools\7zip\7z.exe
Tools\7zip\7z.dll
Tools\7zip\License.txt
```

The project copies `Tools\7zip\**\*` to build and publish output when those files are present.

If 7-Zip is not available at runtime, the application uses managed archive paths where supported.

Hakamiq CHD Tool is not affiliated with, sponsored by, or endorsed by Igor Pavlov or the 7-Zip project.
