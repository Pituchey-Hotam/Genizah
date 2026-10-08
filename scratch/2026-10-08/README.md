`dlgicon.py` drew `Genizah/Resources/Settings.ico`, the censor style dialog's title-bar icon: the
ribbon icon's blue ה׳ badge on its own, laid out on a 16-pixel grid and rendered at 8× then
downsampled for each size (16–256). Run `py -3.13 dlgicon.py <outdir>` (needs Pillow); it writes
the `.ico` plus enlarged previews.
