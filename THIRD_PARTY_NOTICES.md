# Third-party notices

ROG Peripheral is a derivative of [G-Helper](https://github.com/seerge/g-helper), commit af7e1c002b1597269d8a977f2464fdab4f5d9fe6, under GPL-3.0 (see LICENSE). Original authors and contributors include seerge and IceStormNG. UI, localized strings, resources, peripheral models and USB communication were extracted from G-Helper. The communication source also credits vddCore/Starlight; original comments are retained.

Modified files include the peripheral provider (standalone lifecycle), peripheral setting windows (removed laptop Aura synchronization), and mouse binding error handling. New application entry point, configuration store, macro editor, macro execution/dispatch and tests are provided under the same GPL-3.0 license.

Modification date: 2026-10-21. Additional changes include JSON/CSV/AHK macro conversion, three macro trigger modes, fixed vertical layout, a new application icon, and theme/localization integration. See docs/CHANGELOG.md.

Dependency license texts are included in licenses/:

- HidSharpCore 1.3.0: Apache-2.0; Copyright 2010-2019 James F. Bellinger. Source: https://github.com/OpenTabletDriver/HIDSharpCore/tree/bca4aee1c349985b0119eed24aba83e2dd63492d . Includes its NOTICE.txt.
- WinForms.DataVisualization 1.10.2: MIT; .NET Foundation and Contributors. Source: https://github.com/kirsan31/winforms-datavisualization/tree/063510db7fa1e7fafbb19a6b1c79a8f25112a700 . Includes its third-party notices.
- Microsoft .NET / Windows Desktop runtime: license and third-party notices initially copied from the 8.0.3 runtime packs. The build script refreshes these in the distribution from the runtime packs actually resolved by the SDK.

Inherited UI image resources include Icons8 assets (https://icons8.com); upstream resource names and attribution are retained. The new large-G application icon in assets/app-icon.png was generated for this derivative project; it is not an official ASUS or G-Helper logo. Source PNG and icon conversion script are included.

ASUS, ROG, TUF and Armoury Crate are trademarks of their respective owners. Product names and inherited assets describe compatibility; no affiliation or endorsement is claimed.

Research reference (no code imported): https://github.com/sadam266/rog-mouse (316da94e2964707e4bcd28c16a5bd06573b162d8).
