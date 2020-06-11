﻿using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml;
using mdoc.Test.SampleClasses;
using Mono.Cecil;
using Mono.Cecil.Rocks;
using Mono.Collections.Generic;
using Mono.Documentation;
using Mono.Documentation.Updater;
using Mono.Documentation.Updater.Formatters;
using Mono.Documentation.Updater.Frameworks;
using NUnit.Framework;
using Cpp = Mono_DocTest_Generic;

namespace mdoc.Test
{
    [TestFixture]
    public class MDocUpdaterTests : BasicTests
    {
        readonly MDocUpdater updater = new MDocUpdater();
        readonly AttributeFormatter formatter = new AttributeFormatter();
        [Test]
        public void Test_GetCustomAttributes_IgnoredObsoleteAttribute()
        {
            TypeDefinition testType = GetType(typeof(MDocUpdaterTests).Module.FullyQualifiedName, "System.Span`1");
            Collection<CustomAttribute> attributes = testType.CustomAttributes;

            IEnumerable<string> customAttributes = formatter.GetCustomAttributes(attributes, "");

            Assert.AreEqual(1, attributes.Count);
            Assert.IsEmpty(customAttributes);
        }

        [Test]
        public void Test_GetDocParameterType_CppGenericParameterType_ReturnsTypeWithGenericParameters()
        {
            var method = GetMethod(typeof(Cpp.GenericBase<>), "BaseMethod2");

            string parameterType = MDocUpdater.GetDocParameterType(method.Parameters[0].ParameterType);

            Assert.AreEqual("Mono_DocTest_Generic.GenericBase<U>", parameterType);
        }
        [Test]
        public void Test_GetNamespace_IgnoredNamespaceGeneric()
        {
            TypeDefinition testType = GetType(typeof(ReadOnlySpan<>).Module.FullyQualifiedName, "mdoc.Test.SampleClasses.ReadOnlySpan`1");
            var ns = DocUtils.GetNamespace(testType.GenericParameters.First());
            Assert.AreEqual("", ns);
        }

        [Test]
        public void InternalEIITest()
        {
            XmlDocument doc = new System.Xml.XmlDocument();
            doc.LoadXml(XmlConsts.internalEllXml);

            MemberReference oldmember = null;
            var type = GetType(typeof(mdoc.Test2.InternalEIICalss));
            var docEnum = new DocumentationEnumerator();

            bool internalEIIflagged = false;
            foreach (DocsNodeInfo info in docEnum.GetDocumentationMembers(doc, type, FrameworkTypeEntry.Empty))
            {
                var flag = MDocUpdater.IsMemberNotPrivateEII(info.Member);

                if (!flag)
                {
                    internalEIIflagged = true;
                    oldmember = info.Member;
                    //Note : The following operation will not be carried out, just prompt
                      //-> DeleteMember();
                      //-> statisticsCollector.AddMetric();
                }
            }
            Assert.IsTrue(internalEIIflagged, "Internal EII was not flagged");
            Assert.AreEqual("System.String mdoc.Test2.InternalEIICalss::mdoc.Test.SampleClasses.InterfaceA.Getstring(System.Int32)", oldmember.FullName);

        }

        [Test]
        public void RemoveInvalidAssembliesInfo()
        {
            XmlDocument doc = new System.Xml.XmlDocument();
            doc.LoadXml(XmlConsts.internalEllXml);

            var type = GetType(typeof(mdoc.Test2.InternalEIICalss));
            var docEnum = new DocumentationEnumerator();

            var delList = DocUtils.RemoveInvalidAssemblyInfo(doc.DocumentElement, false, "Type");
            Assert.IsTrue(delList.Count == 1);

            foreach (DocsNodeInfo info in docEnum.GetDocumentationMembers(doc, type, FrameworkTypeEntry.Empty))
            {
                delList.AddRange(DocUtils.RemoveInvalidAssemblyInfo(info.Node, false, "Member"));
            }

            Assert.IsTrue(delList.Count == 2);

            ///Note : (The following operation will not be carried out, just prompt)
            //   foreach (var delitem in delList)
            // delitem.ParentNode.RemoveChild(child);        
        }

        [Test]
        public void UpdateToRight_MethodInterface_Test()
        {
            var member = GetType(typeof(mdoc.Test2.EiiImplementClass)).Methods.FirstOrDefault(t => t.FullName == "System.String mdoc.Test2.EiiImplementClass::GetNo()");
            var nodeinfo = UpdateXml("GetNo", member);
            Assert.IsTrue(nodeinfo.Count() == 1);
            Assert.AreEqual("M:mdoc.Test2.Interface_B.GetNo", nodeinfo[0].InnerText);
            Assert.AreEqual("dotnet-plat-ext-2.2", nodeinfo[0].GetAttribute(Consts.FrameworkAlternate));
        }

        [Test]
        public void UpdateToRight_PropertyInterface_Test()
        {
            var member = GetType(typeof(mdoc.Test2.EiiImplementClass)).Properties.FirstOrDefault(t => t.FullName == "System.Int32 mdoc.Test2.EiiImplementClass::no()");
            var nodeinfo = UpdateXml("no", member);
            Assert.IsTrue(nodeinfo.Count() == 1);
            Assert.AreEqual("P:mdoc.Test2.Interface_A.no", nodeinfo[0].InnerText);
            Assert.AreEqual("dotnet-plat-ext-2.2", nodeinfo[0].GetAttribute(Consts.FrameworkAlternate));
        }

        [Test]
        public void UpdateToRight_EventInterface_Test()
        {
            var member = GetType(typeof(mdoc.Test2.EiiImplementClass)).Events.FirstOrDefault(t => t.FullName == "System.EventHandler`1<System.EventArgs> mdoc.Test2.EiiImplementClass::ItemChanged");
            var nodeinfo = UpdateXml("ItemChanged", member);
            Assert.IsTrue(nodeinfo.Count() == 1);
            Assert.AreEqual("E:mdoc.Test2.Interface_A.ItemChanged", nodeinfo[0].InnerText);
            Assert.AreEqual("dotnet-plat-ext-2.2", nodeinfo[0].GetAttribute(Consts.FrameworkAlternate));
        }

        private List<XmlElement> UpdateXml(string XmlNodeName, MemberReference mi)
        {
            List<XmlElement> returnValue = new List<XmlElement>();

            List<FrameworkEntry> entries = new List<FrameworkEntry>();
            FrameworkEntry singleEntry = new FrameworkEntry(entries, entries);
            singleEntry.Name = "dotnet-plat-ext-2.2";
            FrameworkTypeEntry enttyType = new FrameworkTypeEntry(singleEntry);

            var type = GetType(typeof(mdoc.Test2.EiiImplementClass));
            var ieeImplementList = MDocUpdater.GetTypeEiiMembers(type);
            var typeInterfaces = GetClassInterface(type);

            var doc = new XmlDocument();
            doc.LoadXml(XmlConsts.EiiErrorImplement);

            var node = doc.SelectSingleNode($"/Type/Members/Member[@MemberName='{XmlNodeName}']");

            if (node != null)
            {
                MDocUpdater.AddImplementedMembers(enttyType, mi, typeInterfaces, (XmlElement)node, ieeImplementList);
                returnValue = node.SelectNodes("Implements/InterfaceMember").Cast<XmlElement>().ToList();
            }

            return returnValue;
        }
    }
}