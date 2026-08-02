# Third-Party Notices

CsoKit uses a few third-party compression libraries in the native backend.

These components are not owned by HAKAMIQ. Each one stays under its original license.

## zlib

- Component: zlib compression library
- Upstream: https://github.com/madler/zlib
- Native build version: v1.3.2
- License: zlib License

CsoKit uses zlib for raw-Deflate candidate trials, including default, filtered, Huffman-only, and RLE strategies.

## libdeflate

- Component: libdeflate
- Upstream: https://github.com/ebiggers/libdeflate
- Native build version: v1.25
- License: MIT License

CsoKit uses libdeflate for raw-Deflate candidate trials at several compression levels.

## Zopfli

- Component: Zopfli Compression Algorithm
- Upstream: https://github.com/google/zopfli
- Local source path: native/third_party/zopfli
- License: Apache License, Version 2.0
- License file: native/third_party/zopfli/COPYING

Zopfli is only used when `--zopfli` is explicitly requested. Normal compression profiles do not enable it automatically.

## Notes

The managed Deflate path remains available without the native backend.

See LICENSE.txt for CsoKit license terms.