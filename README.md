# ProteaseGuru: Free and Open-Source Tool for In Silico Database Digestion

[![Release](https://img.shields.io/github/v/release/smith-chem-wisc/ProteaseGuru)](https://github.com/smith-chem-wisc/ProteaseGuru/releases/latest)
[![Github All Releases](https://img.shields.io/github/downloads/smith-chem-wisc/ProteaseGuru/total.svg)](https://github.com/smith-chem-wisc/ProteaseGuru/releases)

Download the current version [here](https://github.com/smith-chem-wisc/ProteaseGuru/releases/latest).

ProteaseGuru is a in silico digestion tool for the planning of bottom-up proteomic experiments. ProteaseGuru allows for the digestion of one or more protein
databases with as many proteases as desired. Results of the various proteolytic digests can be visualized with histograms as well as protein sequence coverage maps.

Check out the [wiki page](https://github.com/smith-chem-wisc/ProteaseGuru/wiki) for software details!

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
  * .NET Core 3.1:
     * Windows: https://dotnet.microsoft.com/download/dotnet-core/thank-you/runtime-desktop-3.1.3-windows-x64-installer
* Note that ProteaseGuru only works on Windows at this time.
* 8 GB RAM recommended

## Database Requirements

UniProt .XML or .fasta format; may be used in compressed (.gz) format.

## mzLib


[mzLib](https://github.com/smith-chem-wisc/mzLib) is a [nuget](https://www.nuget.org/packages/mzLib/) package that we created as an all-purpose toolchest for mass-spec data 
analysis and many of its functions provide the tools for MetaMorpheus. mzLib is freely available for use in mass-spec applications. You do not need to download mzLib separately
to run MetaMorpheus; it is already included.

## References
