/*
 * Tencent is pleased to support the open source community by making InjectFix available.
 * Copyright (C) 2019 THL A29 Limited, a Tencent company.  All rights reserved.
 * InjectFix is licensed under the MIT License, except for the third-party components listed in the file 'LICENSE' which may be subject to their corresponding license terms. 
 * This file is subject to the terms and conditions defined in file 'LICENSE', which is part of this source code package.
 */

using System;
using System.IO;
using Mono.Cecil;
using System.Linq;

namespace IFix
{
    public static class Program
    {
        static void usage()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("IFix -inject core_assmbly_path assmbly_path config_path patch_file_output_path"
                + " injected_assmbly_output_path [search_path1, search_path2 ...]");
            Console.WriteLine("IFix -inherit_inject core_assmbly_path assmbly_path config_path "
                + "patch_file_output_path injected_assmbly_output_path inherit_assmbly_path "
                + "[search_path1, search_path2 ...]");
            Console.WriteLine("IFix -patch core_assmbly_path assmbly_path injected_assmbly_path"
                + " config_path patch_file_output_path [search_path1, search_path2 ...]");
        }

        static bool argsValid(string[] args)
        {
            return (args[0] == "-inject" && args.Length >= 6) || (args[0] == "-patch" && args.Length >= 6)
                || (args[0] == "-inherit_inject" && args.Length >= 7);
        }

        // #lizard forgives
        static void Main(string[] args)
        {
            if (!argsValid(args))
            {
                usage();
                return;
            }
            ProcessMode mode = args[0] == "-patch" ?  ProcessMode.Patch : ProcessMode.Inject;

            CodeTranslator tranlater = new CodeTranslator();
            AssemblyDefinition assembly = null;
            AssemblyDefinition ilfixAassembly = null;
            AssemblyDefinition oldAssembly = null;
            string dllName = null;
            try
            {
                var assmeblyPath = args[2];
                bool readSymbols = true;
                try
                {
                    //���Զ�ȡ����
                    assembly = AssemblyDefinition.ReadAssembly(assmeblyPath,
                        new ReaderParameters { ReadSymbols = false });
                }
                catch
                {
                    //�����ȡ���������򲻶�
                    Console.WriteLine("Warning: read " + assmeblyPath + " with symbol fail");
                    //д���ʱ���������־
                    readSymbols = false;
                    assembly = AssemblyDefinition.ReadAssembly(assmeblyPath,
                        new ReaderParameters { ReadSymbols = false });
                }

                var resolver = assembly.MainModule.AssemblyResolver as BaseAssemblyResolver;
                bool isInheritInject = args[0] == "-inherit_inject";
                int searchPathStart = isInheritInject ? 7 : 6;

                //��resolver�����������·��
                foreach (var path in args.Skip(searchPathStart))
                {
                    try
                    {
                        //Console.WriteLine("searchPath:" + path);
                        resolver.AddSearchDirectory(path);
                    } catch { }
                }

                ilfixAassembly = AssemblyDefinition.ReadAssembly(args[1]);
                dllName = Path.GetFileName(args[2]);
                GenerateConfigure configure = null;

                if (mode == ProcessMode.Inject)
                {
                    //�Բ����������⴦����������Ĭ��ȫ����ִ��
                    configure = args[3] == "no_cfg" ?
                        GenerateConfigure.Empty() : GenerateConfigure.FromFile(args[3]);

                    if (isInheritInject)
                    {
                        throw new Exception("Do Not Support already!");
                    }

                    //ע���߼�
                    //TODO: tranlater�����ֲ�̫����
                    if (tranlater.Process(assembly, ilfixAassembly, configure, mode)
                        == CodeTranslator.ProcessResult.Processed)
                    {
                        Console.WriteLine(dllName + " process yet!");
                        return;
                    }

                    tranlater.Serialize(args[4]);

                    assembly.Write(args[5], new WriterParameters { WriteSymbols = false });
                    //ilfixAassembly.Write(args[2], new WriterParameters { WriteSymbols = true });
                }
                else
                {
                    //������������
                    configure = new PatchGenerateConfigure(assembly, args[4]);

                    if (tranlater.Process(assembly, ilfixAassembly, configure, mode)
                        == CodeTranslator.ProcessResult.Processed)
                    {
                        //���ֳ����Ѿ���ע�룬��Ҫ�Ƿ�ֹ�Ѿ�ע��ĺ���������ע���߼��ᵼ����ѭ��
                        Console.WriteLine("Error: the new assembly must not be inject, please reimport the project!");
                        return;
                    }

                    tranlater.Serialize(args[5]);
                    Console.WriteLine("output: " + args[5]);
                }
            }
            catch(Exception e)
            {
                Console.WriteLine("Unhandled Exception:\r\n" + e);
                return;
            }
            finally
            {
                //������Ŷ�ȡ��
                //�����������window�»������ļ�
                if (assembly != null && assembly.MainModule.SymbolReader != null)
                {
                    assembly.MainModule.SymbolReader.Dispose();
                }

                if (ilfixAassembly != null && ilfixAassembly.MainModule.SymbolReader != null)
                {
                    ilfixAassembly.MainModule.SymbolReader.Dispose();
                }

                if (oldAssembly != null && oldAssembly.MainModule.SymbolReader != null)
                {
                    oldAssembly.MainModule.SymbolReader.Dispose();
                }
            }
            Console.WriteLine(dllName + " process success");
        }
    }
}