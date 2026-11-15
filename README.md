# ProteaseGuru: Free and Open-Source Tool for In Silico Database Digestion

[![Release](https://img.shields.io/github/v/release/smith-chem-wisc/ProteaseGuru)](https://github.com/smith-chem-wisc/ProteaseGuru/releases/latest)
[![Github All Releases](https://img.shields.io/github/downloads/smith-chem-wisc/ProteaseGuru/total.svg)](https://github.com/smith-chem-wisc/ProteaseGuru/releases)

Download the current version [here](https://github.com/smith-chem-wisc/ProteaseGuru/releases/latest).

ProteaseGuru is a in silico digestion tool for the planning of bottom-up proteomic experiments. ProteaseGuru allows for the digestion of one or more protein
databases with as many proteases as desired. Results of the various proteolytic digests can be visualized with histograms as well as protein sequence coverage maps.

Check out the [wiki page](https://github.com/smith-chem-wisc/ProteaseGuru/wiki) for software details!

## Getting Started

ProteaseGuru is a **Windows desktop application**. To run your first *in silico* digestion:

**1. Get the app**
* Install the free [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) (Windows x64) if you don't already have it.
* Download `ProteaseGuru.zip` from the [latest release](https://github.com/smith-chem-wisc/ProteaseGuru/releases/latest) and unzip it to a folder you can write to (not `Program Files`).
* Double-click **`ProteaseGuru.exe`** to launch. *(Prefer to build it yourself? See [Build from source](#build-from-source).)*

**2. Run a digestion in 5 steps**
1. **Add a database** — on the *Databases* window, click **Add** (or drag & drop) a UniProt `.xml` or `.fasta` file (`.gz` is fine). Add as many as you like.
2. **Set digestion conditions** — pick one or more proteases and set your parameters (missed cleavages, min/max peptide length, optional mass range and modifications).
3. **Review & run** — the *Run* window summarizes your databases, proteases, parameters, and output location. Click **Run**.
4. **Explore results** — when digestion finishes, the *Results Summary* opens. Use **Histograms** to compare proteases across the whole proteome, or **Protein Search** to view per-protein sequence-coverage maps.
5. **Export** — result tables are written to the output folder automatically; histograms export as PDF + reproducible `.csv`, and coverage maps export from the Protein Search window.

For a walkthrough of each window, see the [wiki Getting Started page](https://github.com/smith-chem-wisc/ProteaseGuru/wiki/Getting-Started).

### Command-line runs

The `ProteaseGuru.Cli` executable runs the digestion pipeline without opening the desktop interface. It accepts one or more `.fasta`, `.fa`, or `.xml` databases (optionally `.gz` compressed), and writes the same digestion outputs as a GUI run to the specified output directory.

Use a saved `DigestionParameters.toml` when you need the full GUI configuration, including protease-specific values and modifications. Any explicitly supplied digestion option overrides that value from the TOML for all selected proteases/RNases, while unspecified values and modifications stay as configured in the file:

```powershell
ProteaseGuru.Cli.exe --database proteins.fasta --output results --params DigestionParameters.toml
ProteaseGuru.Cli.exe --database proteins.fasta --output results-mc3 `
  --params DigestionParameters.toml --missed-cleavages 3
```

Or supply shared digestion values for all selected proteases/RNases. Values shown below are defaults; `--rna` enables RNA mode and otherwise the run is protein mode:

```powershell
ProteaseGuru.Cli.exe --database proteins.fasta --database contaminants.fasta `
  --output results --protease "trypsin|P" --protease "Lys-C|P" `
  --missed-cleavages 2 --min-length 7 --max-length 50 `
  --min-mass -1 --max-mass -1 --detectability-threshold 0.5
```

For RNA mode, use RNase names and add `--rna`. Multiple database and protease options can be space-delimited or repeated. In TOML mode the TOML file supplies protease selection; `--protease` cannot be combined with it. Shared CLI overrides include missed cleavages, min/max length, min/max mass, detectability threshold, and modified-sequence uniqueness (including explicit `true` or `false`). Each supplied override applies uniformly to all proteases/RNases in the TOML. Run `ProteaseGuru.Cli.exe --help` for all options.

### Build from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) and Windows.

```
git clone https://github.com/smith-chem-wisc/ProteaseGuru.git
cd ProteaseGuru
dotnet build ProteaseGuru.sln -c Release
dotnet run --project ProteaseGuruGui/ProteaseGuru.Gui.csproj
```

Build and run the CLI with:

```
dotnet run --project ProteaseGuru.Cli/ProteaseGuru.Cli.csproj -- --help
```

Or open `ProteaseGuru.sln` in Visual Studio 2022, set **ProteaseGuru.Gui** as the startup project, and press **F5**.

## Major Features
* Ability to digest more than one database for application with multi-species samples such as xenografts, virally infected host cells and microbiome samples.
* In silico digestion with as many proteases as desired!
* Ability to define custom proteases for digestion.
* Uniqueness of peptide sequences are determined both within a database and across all databases being analyzed.
* Visualization of whole proteome digestion results is enabled by the generation and ability to export histograms.
* Search for your proteins of interest and view their in silico digestion with a sequence coverage map with PTM and variant annotation!

## System Requirements

* Environment:
  * 64-bit operating system
  * .NET Core 8.0:
    * Windows: https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/sdk-8.0.401-windows-x64-installer
    * macOS, x64 Intel processor: https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/sdk-8.0.401-macos-x64-installer
    * macOS, ARM Apple Silicon processor: https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/sdk-8.0.401-macos-arm64-installer
    * Linux: https://learn.microsoft.com/dotnet/core/install/linux?WT.mc_id=dotnet-35129-website
* Note that ProteaseGuru only works on Windows at this time.
* 8 GB RAM recommended

## Database Requirements

UniProt .XML or .fasta format; may be used in compressed (.gz) format.

## mzLib


[mzLib](https://github.com/smith-chem-wisc/mzLib) is a [nuget](https://www.nuget.org/packages/mzLib/) package that we created as an all-purpose toolchest for mass-spec data 
analysis and many of its functions provide the tools for MetaMorpheus. mzLib is freely available for use in mass-spec applications. You do not need to download mzLib separately
to run MetaMorpheus; it is already included.

## References
