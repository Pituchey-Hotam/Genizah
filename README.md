# Genizah

A Word add-in that finds the divine names in a Hebrew document and censors them for you.

Once one of the names is written, the paper it's on can't simply be thrown away; it needs
genizah. Writing the name in a modified form (אלקים for אלהים, ה' for יהוה) avoids that, but
going through a long document by hand is slow and easy to get wrong. And it isn't a matter of
search and replace: אל is usually just "to", and אלהים can mean judges or foreign gods. Genizah
reads each occurrence in its context, censors the ones it's sure are divine names, leaves the
ones it's sure aren't, and asks you about the rest.

![The review pane beside a document](docs/images/review-pane.png)

## How it works

1. **Scan.** Click **סקירת שמות** in the **גניזה** group on Word's **סקירה** (Review) tab. Genizah scans the
   document and opens a review pane beside it.
2. **It sorts the names.** Every name it finds goes into one of three groups:
   - **לצנזר**: divine names. Genizah censors these right away.
   - **להשאיר**: ordinary words that happen to share the spelling. Left as they are.
   - **לא בטוח**: the ones it isn't sure about. These wait for you.
3. **Review.** Go through **לא בטוח**. Each row shows the name in its sentence; select it and
   Word jumps to the spot in the document. Choose **צנזר** or **השאר**. You can also check the
   other two groups and change any decision; a censored name can be restored.

   ![Deciding an uncertain name](docs/images/review-ask.png)

4. **Print.** If you print with names still waiting for a decision, or without having
   reviewed the document, Genizah asks first, so that nothing goes out uncensored by
   accident.

   ![The check before printing](docs/images/print-check.png)

## Installing

Download `Genizah-Setup-<version>.exe` from the
[latest release](https://github.com/Pituchey-Hotam/Genizah/releases/latest) and run it. It
needs Word for Windows (64-bit). The model is included and runs on your own computer, so your
documents never leave it and no internet connection is needed. When one is available, Genizah
updates itself in the background.

**Genizah is an aid, not a guarantee.** It can miss a name or censor a word that isn't one;
review the document before relying on it.

## For developers

Building needs Visual Studio with the Office/SharePoint development workload.

```
powershell -File tools/dist/fetch_model.ps1      # the model graph (~100 MB, not in git)
MSBuild.exe Genizah/Genizah.csproj -restore      # not `dotnet build`: VSTO needs MSBuild
dotnet test Genizah.Core.Tests                   # net48 and net7.0
```

- `Genizah/`: the VSTO add-in (.NET Framework 4.8, x64): ribbon, scanning, review pane.
- `Genizah.Core/`: the classifier's input pipeline and ONNX scoring, with no Office dependency
  (netstandard2.0 and net48).
- `Genizah.Core.Tests/`: xUnit, against test vectors generated from the training code.
- `model/`: vocabulary, manifest and per-name thresholds. The `.onnx` graph is fetched.
- `tools/`: release scripts (`dist/`) and a scan benchmark that drives Word (`benchmark/`).

Releases and updates: [`docs/RELEASING.md`](docs/RELEASING.md).

## License and credits

Apache License 2.0 (`LICENSE`), for the code and the model. The model is built on DICTA's
BEREL 3.0 and learned from the hand-censored issues of עולה מן המדבר, the journal of ישיבת
ברכת משה, and from the Hebrew Bible and vocalized liturgy. `NOTICE` credits them all, along with the
third-party software shipped with the add-in, whose licenses are in `licenses/`.
