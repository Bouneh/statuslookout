# Third-party notices

The built-in service definitions are adapted from [inket/stts](https://github.com/inket/stts/blob/master/Resources/services.json), retrieved on 2026-10-12 at commit `876ecac0996c719786477af8e3833cdac05235ba`. All 1048 source entries have a recorded local correspondence in `stts-coverage.json`. The Windows application, UI, state management and parsers are independently implemented. No macOS UI assets or Swift source are shipped.

## Bundled service favicons

268 brand favicons covering the 1092 shipped service entries are copied from [stts/Resources/Favicons](https://github.com/inket/stts/tree/876ecac0996c719786477af8e3833cdac05235ba/Resources/Favicons), pinned to commit `876ecac0996c719786477af8e3833cdac05235ba`. The source and service-to-file mapping are recorded in `src/StatusLookout.Windows/Assets/ServiceLogos/SOURCE.json` and `index.json`. The stts collection is distributed under the MIT license below; brand artwork and trademarks belong to their respective owners. Use here solely identifies monitored services and does not imply endorsement. No brand logo is the StatusLookout application logo.

## stts service catalog and favicon collection — MIT

MIT License

Copyright (c) 2016 Mahdi Bchetnia

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

## Google and Firebase public catalogs

Google Cloud and Firebase product names and stable IDs were retrieved from their public status catalogs on 2026-10-11:

- https://status.cloud.google.com/products.json
- https://status.firebase.google.com/products.json

These entries replace the Google/Firebase name bindings in the stts-derived data. Product names belong to their respective owners.

## Microsoft.Toolkit.Uwp.Notifications

MIT-licensed Windows Community Toolkit notification library, version 7.1.3. Source and license: https://github.com/CommunityToolkit/WindowsCommunityToolkit. See the NuGet package for its dependency notices.

Self-contained releases also distribute the .NET and Windows Desktop runtimes. Their original license files and .NET third-party notices are included as separate release assets, along with the notification library's original MIT license.

Service names remain the property of their respective owners. StatusLookout is not affiliated with those providers.

## Stream Deck companion

The plugin uses the MIT-licensed official Elgato SDK (`@elgato/streamdeck`, `@elgato/utils`, `@elgato/schemas`) and the MIT-licensed `ws` library. Exact versions are pinned in `plugins/streamdeck/package-lock.json`. Their original license texts are included inside the `.streamDeckPlugin` package as `THIRD-PARTY-NOTICES.txt`. Source: https://github.com/elgatosf/streamdeck and https://github.com/websockets/ws.

## AWS public catalog

AWS products and regions come from the public AWS Health dashboard asset at https://d3s31nlw3sm5l8.cloudfront.net/services.json, retrieved on 2026-10-12. Events come from https://health.aws.amazon.com/public/currentevents. AWS names and artwork belong to their respective owners.

## Html Agility Pack

Version 1.13.0, MIT licensed. Copyright © ZZZ Projects Inc. Used to read public HTML status dashboards. Source and license: https://github.com/zzzprojects/html-agility-pack/blob/master/LICENSE. The original license is distributed in `LICENSE-html-agility-pack.txt`.
