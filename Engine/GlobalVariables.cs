using Chemistry;
using Omics.Modifications;
using Omics.Modifications.IO;
using Proteomics.AminoAcidPolymer;
using Proteomics.ProteolyticDigestion;
using System.Diagnostics;
using Omics.Modifications;
using Omics.Modifications.IO;

namespace Engine
{
    public static class GlobalVariables
    {
        private static List<Modification> _AllModsKnown = new List<Modification>();
        private static HashSet<string> _AllModTypesKnown = new HashSet<string>();
        public static List<Modification> ProteaseMods = new List<Modification>();

        // Characters that aren't amino acids, but are reserved for special uses (motifs, delimiters, mods, etc)
        private static char[] _InvalidAminoAcids = new char[] { 'X', 'B', 'J', 'Z', ':', '|', ';', '[', ']', '{', '}', '(', ')', '+', '-' };

        // this affects output labels, etc. and can be changed to "Proteoform" for top-down searches
        public static string AnalyteType = "Peptide";

        static GlobalVariables()
        {
            var version = typeof(GlobalVariables).Assembly.GetName().Version;
            ProteaseGuruVersion = version?.ToString() ?? "Unknown";

            if (ProteaseGuruVersion.Equals("1.0.0.0"))
            {
#if DEBUG
                ProteaseGuruVersion = "Not a release version. DEBUG.";
#else
                ProteaseGuruVersion = "Not a release version. RELEASE";
#endif
            }
            else
            {
                // AppVeyor appends the build number; trim it for display
                var foundIndexes = new List<int>();
                for (int i = 0; i < ProteaseGuruVersion.Length; i++)
                {
                    if (ProteaseGuruVersion[i] == '.')
                    {
                        foundIndexes.Add(i);
                    }
                }
                ProteaseGuruVersion = ProteaseGuruVersion.Substring(0, foundIndexes.Last());
            }

            {
                var pathToProgramFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                if (!String.IsNullOrWhiteSpace(pathToProgramFiles) && AppDomain.CurrentDomain.BaseDirectory.Contains(pathToProgramFiles) && !AppDomain.CurrentDomain.BaseDirectory.Contains("Jenkins"))
                {
                    DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProteaseGuru");
                }
                else
                {
                    DataDir = AppDomain.CurrentDomain.BaseDirectory;
                }
            }

            ElementsLocation = Path.Combine(DataDir, @"Data", @"elements.dat");

            // Load Unimod modifications from XML
            UnimodDeserialized = ModificationLoader.ReadModsFromUnimod(
                Path.Combine(DataDir, @"Data", @"unimod.xml")).ToList();

            // Load PSI-MOD for formal charges lookup
            PsiModDeserialized = ModificationLoader.LoadPsiMod(
                Path.Combine(DataDir, @"Data", @"PSI-MOD.obo.xml"));
            var formalChargesDictionary = ModificationLoader.GetFormalChargesDictionary(PsiModDeserialized);

            // Load UniProt modifications (ptmlist.txt uses the formalCharges overload)
            UniprotDeseralized = ModificationLoader.ReadModsFromFile(
                Path.Combine(DataDir, @"Data", @"ptmlist.txt"),
                formalChargesDictionary,
                out var uniprotWarnings).ToList();
            foreach (var (_, warning) in uniprotWarnings)
            {
                ErrorsReadingMods.Add(warning);
            }

            // Load all mod files from the Mods folder
            foreach (var modFile in Directory.GetFiles(Path.Combine(DataDir, @"Mods")))
            {
                AddMods(ModificationLoader.ReadModsFromFile(modFile, out var filteredMods), false);
                foreach (var (_, warning) in filteredMods)
                {
                    ErrorsReadingMods.Add(warning);
                }
            }

            AddMods(UniprotDeseralized.OfType<Modification>(), false);
            AddMods(UnimodDeserialized.OfType<Modification>(), false);

            // Populate dictionary of known mods for deserialization
            AllModsKnownDictionary = new Dictionary<string, Modification>();
            foreach (Modification mod in AllModsKnown)
            {
                if (!AllModsKnownDictionary.ContainsKey(mod.IdWithMotif))
                {
                    AllModsKnownDictionary.Add(mod.IdWithMotif, mod);
                }
                // no error thrown if multiple mods with this ID are present - just pick one
            }

            ProteaseMods = ModificationLoader.ReadModsFromFile(Path.Combine(DataDir, @"Mods", @"ProteaseMods.txt"), out var errors).ToList();
            ProteaseDictionary.LoadAndMergeCustomProteases(Path.Combine(DataDir, @"ProteolyticDigestion", @"proteases.tsv"), ProteaseMods);

            RefreshAminoAcidDictionary();
        }

        public static List<string> ErrorsReadingMods = new List<string>();

        // File locations
        public static string DataDir { get; }
        public static bool StopLoops { get; set; }
        public static string ElementsLocation { get; }
        public static string ProteaseGuruVersion { get; }
        public static IEnumerable<Modification> UnimodDeserialized { get; }
        public static IEnumerable<Modification> UniprotDeseralized { get; }
        public static obo PsiModDeserialized { get; }
        public static IEnumerable<Modification> AllModsKnown { get { return _AllModsKnown.AsEnumerable(); } }
        public static IEnumerable<string> AllModTypesKnown { get { return _AllModTypesKnown.AsEnumerable(); } }
        public static Dictionary<string, Modification> AllModsKnownDictionary { get; private set; }
        public static IEnumerable<char> InvalidAminoAcids { get { return _InvalidAminoAcids.AsEnumerable(); } }

        public static void AddMods(IEnumerable<Modification> modifications, bool modsAreFromTheTopOfProteinXml)
        {
            foreach (var mod in modifications)
            {
                if (string.IsNullOrEmpty(mod.ModificationType) || string.IsNullOrEmpty(mod.IdWithMotif))
                {
                    ErrorsReadingMods.Add(mod.ToString() + Environment.NewLine + " has null or empty modification type");
                    continue;
                }
                if (AllModsKnown.Any(b => b.IdWithMotif.Equals(mod.IdWithMotif) && b.ModificationType.Equals(mod.ModificationType) && !b.Equals(mod)))
                {
                    if (modsAreFromTheTopOfProteinXml)
                    {
                        _AllModsKnown.RemoveAll(p => p.IdWithMotif.Equals(mod.IdWithMotif) && p.ModificationType.Equals(mod.ModificationType) && !p.Equals(mod));
                        _AllModsKnown.Add(mod);
                        _AllModTypesKnown.Add(mod.ModificationType);
                    }
                    else
                    {
                        ErrorsReadingMods.Add("Modification id and type are equal, but some fields are not! " +
                            "The following mod was not read in: " + Environment.NewLine + mod.ToString());
                    }
                    continue;
                }
                else if (AllModsKnown.Any(b => b.IdWithMotif.Equals(mod.IdWithMotif) && b.ModificationType.Equals(mod.ModificationType)))
                {
                    continue;
                }
                else if (AllModsKnown.Any(m => m.IdWithMotif == mod.IdWithMotif))
                {
                    if (modsAreFromTheTopOfProteinXml)
                    {
                        _AllModsKnown.RemoveAll(p => p.IdWithMotif.Equals(mod.IdWithMotif) && !p.Equals(mod));
                        _AllModsKnown.Add(mod);
                        _AllModTypesKnown.Add(mod.ModificationType);
                    }
                    else if (!mod.ModificationType.Equals("Unimod"))
                    {
                        ErrorsReadingMods.Add("Duplicate mod IDs! Skipping " + mod.ModificationType + ":" + mod.IdWithMotif);
                    }
                    continue;
                }
                else
                {
                    _AllModsKnown.Add(mod);
                    _AllModTypesKnown.Add(mod.ModificationType);
                }
            }
        }

        public static void RefreshAminoAcidDictionary()
        {
            string aminoAcidPath = Path.Combine(DataDir, @"CustomAminoAcids", @"CustomAminoAcids.txt");
            if (File.Exists(aminoAcidPath))
            {
                string[] aminoAcidLines = File.ReadAllLines(aminoAcidPath);
                List<Residue> residuesToAdd = new List<Residue>();
                for (int i = 1; i < aminoAcidLines.Length; i++)
                {
                    string[] line = aminoAcidLines[i].Split('\t').ToArray();
                    if (line.Length >= 4)
                    {
                        char letter = line[1][0];
                        if (InvalidAminoAcids.Contains(letter))
                        {
                            throw new ProteaseGuruException("Error while reading 'CustomAminoAcids.txt'. Line " + (i + 1).ToString() + " contains an invalid amino acid. (Ex: " + string.Join(", ", InvalidAminoAcids.Select(x => x.ToString())) + ")");
                        }
                        try
                        {
                            ChemicalFormula formula = ChemicalFormula.ParseFormula(line[3]);
                            if (!(Residue.TryGetResidue(letter, out Residue residue))
                                || !(formula.Formula.Equals(residue.ThisChemicalFormula.Formula)))
                            {
                                residuesToAdd.Add(new Residue(line[0], letter, line[1], formula, ModificationSites.Any));
                            }
                        }
                        catch
                        {
                            throw new ProteaseGuruException("Error while reading 'CustomAminoAcids.txt'. Line " + (i + 1).ToString() + " was not in the correct format.");
                        }
                    }
                }
                Residue.AddNewResiduesToDictionary(residuesToAdd);
            }
            else
            {
                WriteAminoAcidsFile();
            }
        }

        public static void WriteAminoAcidsFile()
        {
            string directory = Path.Combine(DataDir, @"CustomAminoAcids");
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            string aminoAcidPath = Path.Combine(DataDir, @"CustomAminoAcids", @"CustomAminoAcids.txt");
            List<string> linesToWrite = new List<string> { "Name\tOneLetterAbbr.\tMonoisotopicMass\tChemicalFormula" };
            for (char letter = 'A'; letter <= 'Z'; letter++)
            {
                if (Residue.TryGetResidue(letter, out Residue residue))
                {
                    linesToWrite.Add(residue.Name + '\t' + residue.Letter.ToString() + '\t' + residue.MonoisotopicMass.ToString() + '\t' + residue.ThisChemicalFormula.Formula);
                }
            }
            File.WriteAllLines(aminoAcidPath, linesToWrite.ToArray());
        }

        public static void StartProcess(string path)
        {
            var p = new Process();
            p.StartInfo = new ProcessStartInfo(path)
            {
                UseShellExecute = true
            };
            p.Start();
        }
    }
}
