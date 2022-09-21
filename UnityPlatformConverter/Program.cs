using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using CommandLine;

namespace UnityPlatformConverter
{
    class Program
    {
        AssetsManager am;

        public class Options
        {
            [Option('d', "directoryMode", Required = false, HelpText = "Set to work on directory instead of file")]
            public bool DirectoryMode { get; set; } = false;

            [Option('s', "silent", Required = false, HelpText = "Set as true to hide console messages")]
            public bool Silent { get; set; } = false;

            [Option('p', "platform", Required = true, HelpText = "platform integer. Common platforms: 5-pc 13-android 20-webgl")]
            public int Platform { get; set; }

            [Option('i', "input", Required = true, HelpText = "input file/directory with extension")]
            public string Input { get; set; }

            [Option('o', "output", Required = true, HelpText = "output file/directory with extension")]
            public string Output { get; set; }
        }

        static void Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            Parser.Default.ParseArguments<Options>(args)
                   .WithParsed<Options>(o =>
                   {
                       if (o.DirectoryMode)
                       {
                           Program p = new Program();
                           p.ChangeDirectoryVersion(o.Platform, o.Input, o.Output, o.Silent);
                       }
                       else
                       {
                           Program p = new Program();
                           p.ChangeFileVersion(o.Platform, o.Input, o.Output, o.Silent);
                       }
                   });

            //p.FixPath(args[0], args[1], args[2]);
        }

        private void FixPath(string platformId, string input, string output)
        {
            am = new AssetsManager();
            var assembly = Assembly.GetExecutingAssembly();
            string resourceName = assembly.GetManifestResourceNames().Single(str => str.EndsWith("classdata.tpk"));
            am.LoadClassPackage(assembly.GetManifestResourceStream(resourceName));

            //Load file
            string selectedFile = input;
            BundleFileInstance bundleInst = am.LoadBundleFile(selectedFile, false);

            //Decompress the file to memory
            bundleInst.file = DecompressToMemory(bundleInst);

            AssetsFileInstance inst = am.LoadAssetsFileFromBundle(bundleInst, 0);
            am.LoadClassDatabaseFromPackage(inst.file.typeTree.unityVersion);

            inst.file.typeTree.version = (uint)int.Parse(platformId); //5-pc //13-android //20-webgl

            List<AssetsReplacerFromMemory> replacers = new List<AssetsReplacerFromMemory>();
            List<Int64> filesToAdd = new List<Int64>() { 9185003055734680539 };
            foreach (var inf in inst.table.assetFileInfo)
            {
                AssetTypeValueField baseField = am.GetTypeInstance(inst.file, inf).GetBaseField();
                try
                {
                    //var mContainer = baseField.Get("m_Container").Get("Array");

                    //List<AssetTypeValueField> fields = new List<AssetTypeValueField>();
                    //fields = mContainer.GetChildrenList().ToList();

                    //foreach(var fileID in filesToAdd)
                    //{
                    //    var newValue = ValueBuilder.DefaultValueFieldFromArrayTemplate(mContainer);
                    //    newValue[0].GetValue().Set("assets/parade/prefabs/charas/model/ch_0100_a.prefab");
                    //    newValue[1].Get("asset").Get("m_PathID").GetValue().Set(fileID);
                    //    fields.Add(newValue);
                    //}
                    //mContainer.SetChildrenList(fields.ToArray());

                    var mContainer = baseField.Get("m_PreloadTable").Get("Array");
                    Console.WriteLine("Container found");
                    List<AssetTypeValueField> fields = new List<AssetTypeValueField>();
                    fields = mContainer.GetChildrenList().ToList();

                    foreach (var fileID in filesToAdd)
                    {
                        var newValue = ValueBuilder.DefaultValueFieldFromArrayTemplate(mContainer);
                        newValue.Get("m_FileID").GetValue().Set(0);
                        newValue.Get("m_PathID").GetValue().Set(fileID);
                        fields.Add(newValue);
                    }
                    mContainer.SetChildrenList(fields.ToArray());

                    mContainer = baseField.Get("m_Container").Get("Array");
                    mContainer.children[0][1]["preloadSize"].GetValue().Set(fields.Count);

                    Console.WriteLine("Patch applied");
                    byte[] newGoBytes = baseField.WriteToByteArray();
                    AssetsReplacerFromMemory repl = new AssetsReplacerFromMemory(0, inf.index, (int)inf.curFileType, AssetHelper.GetScriptIndex(inst.file, inf), newGoBytes);
                    replacers.Add(repl);
                }
                catch (Exception ex)
                {
                    //Console.WriteLine(ex.Message);
                }
            }

            //commit changes
            byte[] newAssetData;
            using (MemoryStream stream = new MemoryStream())
            using (AssetsFileWriter writer = new AssetsFileWriter(stream))
            {
                inst.file.Write(writer, 0, new List<AssetsReplacer>(replacers), 0);
                newAssetData = stream.ToArray();
            }

            BundleReplacerFromMemory bunRepl = new BundleReplacerFromMemory(inst.name, null, true, newAssetData, -1);

            //write a modified file (temp)
            string tempFile = Path.GetTempFileName();
            using (var stream = File.OpenWrite(tempFile))
            using (var writer = new AssetsFileWriter(stream))
            {
                bundleInst.file.Write(writer, new List<BundleReplacer>() { bunRepl });
            }
            bundleInst.file.Close();

            //load the modified file for compression
            bundleInst = am.LoadBundleFile(tempFile);
            using (var stream = File.OpenWrite(output))
            using (var writer = new AssetsFileWriter(stream))
            {
                bundleInst.file.Pack(bundleInst.file.reader, writer, AssetBundleCompressionType.LZ4);
            }
            bundleInst.file.Close();

            File.Delete(tempFile);
            Console.WriteLine("complete");
            am.UnloadAll(); //delete this if something breaks
        }

        private void ChangeFileVersion(int platformId, string input, string output, bool silent)
        {
            am = new AssetsManager();
            var assembly = Assembly.GetExecutingAssembly();
            string resourceName = assembly.GetManifestResourceNames().Single(str => str.EndsWith("classdata.tpk"));
            am.LoadClassPackage(assembly.GetManifestResourceStream(resourceName));

            //Load file
            string selectedFile = input;
            BundleFileInstance bundleInst = null;
            try
            {
                bundleInst = am.LoadBundleFile(selectedFile, false);
                //Decompress the file to memory
                bundleInst.file = DecompressToMemory(bundleInst);
            }
            catch
            {
                if (!silent) Console.WriteLine($"Error: {Path.GetFileName(selectedFile)} is not a valid bundle file");
                return;
            }

            AssetsFileInstance inst = am.LoadAssetsFileFromBundle(bundleInst, 0);
            am.LoadClassDatabaseFromPackage(inst.file.typeTree.unityVersion);

            inst.file.typeTree.version = (uint)platformId; //5-pc //13-android //20-webgl

            //commit changes
            byte[] newAssetData;
            using (MemoryStream stream = new MemoryStream())
            {
                using (AssetsFileWriter writer = new AssetsFileWriter(stream))
                {
                    inst.file.Write(writer, 0, new List<AssetsReplacer>() { }, 0);
                    newAssetData = stream.ToArray();
                }
            }

            BundleReplacerFromMemory bunRepl = new BundleReplacerFromMemory(inst.name, null, true, newAssetData, -1);

            //write a modified file (temp)
            string tempFile = Path.GetTempFileName();
            using (var stream = File.OpenWrite(tempFile))
            using (var writer = new AssetsFileWriter(stream))
            {
                bundleInst.file.Write(writer, new List<BundleReplacer>() { bunRepl });
            }
            bundleInst.file.Close();

            //load the modified file for compression
            bundleInst = am.LoadBundleFile(tempFile);
            using (var stream = File.OpenWrite(output))
            using (var writer = new AssetsFileWriter(stream))
            {
                bundleInst.file.Pack(bundleInst.file.reader, writer, AssetBundleCompressionType.LZ4);
            }
            bundleInst.file.Close();

            File.Delete(tempFile);
            if (!silent) Console.WriteLine("complete");
            am.UnloadAll(); //delete this if something breaks
        }

        private void ChangeDirectoryVersion(int platformId, string inputDir, string outputDir, bool silent)
        {
            Directory.CreateDirectory(outputDir);

            am = new AssetsManager();
            var assembly = Assembly.GetExecutingAssembly();
            string resourceName = assembly.GetManifestResourceNames().Single(str => str.EndsWith("classdata.tpk"));
            am.LoadClassPackage(assembly.GetManifestResourceStream(resourceName));
            foreach (var selectedFile in Directory.GetFiles(inputDir))
            {
                if (!silent) Console.WriteLine($"Converting {Path.GetFileName(selectedFile)}");

                //Load file
                BundleFileInstance bundleInst = null;
                try
                {
                    bundleInst = am.LoadBundleFile(selectedFile, false);
                    //Decompress the file to memory
                    bundleInst.file = DecompressToMemory(bundleInst);
                }
                catch
                {
                    if (!silent) Console.WriteLine($"Error: {Path.GetFileName(selectedFile)} is not a valid bundle file");
                    continue;
                }

                AssetsFileInstance inst = am.LoadAssetsFileFromBundle(bundleInst, 0);
                am.LoadClassDatabaseFromPackage(inst.file.typeTree.unityVersion);

                inst.file.typeTree.version = (uint)platformId; //5-pc //13-android //20-webgl

                //commit changes
                byte[] newAssetData;
                using (MemoryStream stream = new MemoryStream())
                {
                    using (AssetsFileWriter writer = new AssetsFileWriter(stream))
                    {
                        inst.file.Write(writer, 0, new List<AssetsReplacer>() { }, 0);
                        newAssetData = stream.ToArray();
                    }
                }

                BundleReplacerFromMemory bunRepl = new BundleReplacerFromMemory(inst.name, null, true, newAssetData, -1);

                //write a modified file (temp)
                string tempFile = Path.GetTempFileName();
                using (var stream = File.OpenWrite(tempFile))
                using (var writer = new AssetsFileWriter(stream))
                {
                    bundleInst.file.Write(writer, new List<BundleReplacer>() { bunRepl });
                }
                bundleInst.file.Close();

                //load the modified file for compression
                bundleInst = am.LoadBundleFile(tempFile);
                using (var stream = File.OpenWrite(Path.Combine(outputDir, Path.GetFileName(selectedFile))))
                using (var writer = new AssetsFileWriter(stream))
                {
                    bundleInst.file.Pack(bundleInst.file.reader, writer, AssetBundleCompressionType.LZ4);
                }
                bundleInst.file.Close();

                File.Delete(tempFile);
                am.UnloadAll(); //delete this if something breaks
            }
            if (!silent) Console.WriteLine("complete");
        }

        public static AssetBundleFile DecompressToMemory(BundleFileInstance bundleInst)
        {
            AssetBundleFile bundle = bundleInst.file;

            MemoryStream bundleStream = new MemoryStream();
            bundle.Unpack(bundle.reader, new AssetsFileWriter(bundleStream));

            bundleStream.Position = 0;

            AssetBundleFile newBundle = new AssetBundleFile();
            newBundle.Read(new AssetsFileReader(bundleStream), false);

            bundle.reader.Close();
            return newBundle;
        }
    }
}
