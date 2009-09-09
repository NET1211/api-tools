using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using System.Xml.Xsl;
using System.Xml.XPath;

using Mono.Documentation;
using Mono.Options;

[assembly: AssemblyTitle("Monodocs-to-HTML")]
[assembly: AssemblyCopyright("Copyright (c) 2004 Joshua Tauberer <tauberer@for.net>, released under the GPL.")]
[assembly: AssemblyDescription("Convert Monodoc XML documentation to static HTML.")]

namespace Mono.Documentation {

class MDocToHtmlConverterOptions {
	public string dest;
	public string ext = "html";
	public string onlytype;
	public string template;
	public bool   dumptemplate;
	public bool   forceUpdate;
}

class MDocToHtmlConverter : MDocCommand {

	public override void Run (IEnumerable<string> args)
	{
		opts = new MDocToHtmlConverterOptions ();
		var p = new OptionSet () {
			{ "default-template",
				"Writes the default XSLT to stdout.",
				v => opts.dumptemplate = v != null },
			{ "ext=",
				"The file {EXTENSION} to use for created files.  "+
					"This defaults to \"html\".",
				v => opts.ext = v },
			{ "force-update",
				"Always generate new files.  If not specified, will only generate a " + 
					"new file if the source .xml file is newer than the current output " +
					"file.",
				v => opts.forceUpdate = v != null },
			{ "o|out=",
				"The {DIRECTORY} to place the generated files and directories.",
				v => opts.dest = v },
			{ "template=",
				"An XSLT {FILE} to use to generate the created " + 
					"files.  If not specified, uses the template generated by " + 
					"--default-template.",
				v => opts.template = v },
		};
		List<string> extra = Parse (p, args, "export-html", 
				"[OPTIONS]+ DIRECTORIES",
				"Export mdoc documentation within DIRECTORIES to HTML.");
		if (extra == null)
			return;
		if (opts.dumptemplate)
			DumpTemplate ();
		else
			ProcessDirectories (extra);
		opts.onlytype = "ignore"; // remove warning about unused member
	}

	static MDocToHtmlConverterOptions opts;

	void ProcessDirectories (List<string> sourceDirectories)
	{
		if (sourceDirectories.Count == 0 || opts.dest == null || opts.dest == "")
			throw new ApplicationException("The source and dest options must be specified.");
		
		Directory.CreateDirectory(opts.dest);

		// Load the stylesheets, overview.xml, and resolver
		
		XslTransform overviewxsl = LoadTransform("overview.xsl", sourceDirectories);
		XslTransform stylesheet = LoadTransform("stylesheet.xsl", sourceDirectories);
		XslTransform template;
		if (opts.template == null) {
			template = LoadTransform("defaulttemplate.xsl", sourceDirectories);
		} else {
			try {
				XmlDocument templatexsl = new XmlDocument();
				templatexsl.Load(opts.template);
				template = new XslTransform();
				template.Load(templatexsl);
			} catch (Exception e) {
				throw new ApplicationException("There was an error loading " + opts.template, e);
			}
		}
		
		XmlDocument overview = GetOverview (sourceDirectories);
		string overviewDest   = opts.dest + "/index." + opts.ext;

		ArrayList extensions = GetExtensionMethods (overview);
		
		// Create the master page
		XsltArgumentList overviewargs = new XsltArgumentList();

		var regenIndex = sourceDirectories.Any (
					d => !DestinationIsNewer (Path.Combine (d, "index.xml"), overviewDest));
		if (regenIndex) {
			overviewargs.AddParam("ext", "", opts.ext);
			overviewargs.AddParam("basepath", "", "./");
			Generate(overview, overviewxsl, overviewargs, opts.dest + "/index." + opts.ext, template, sourceDirectories);
			overviewargs.RemoveParam("basepath", "");
		}
		overviewargs.AddParam("basepath", "", "../");
		overviewargs.AddParam("Index", "", overview.CreateNavigator ());
		
		// Create the namespace & type pages
		
		XsltArgumentList typeargs = new XsltArgumentList();
		typeargs.AddParam("ext", "", opts.ext);
		typeargs.AddParam("basepath", "", "../");
		typeargs.AddParam("Index", "", overview.CreateNavigator ());
		
		foreach (XmlElement ns in overview.SelectNodes("Overview/Types/Namespace")) {
			string nsname = ns.GetAttribute("Name");

			if (opts.onlytype != null && !opts.onlytype.StartsWith(nsname + "."))
				continue;
				
			System.IO.DirectoryInfo d = new System.IO.DirectoryInfo(opts.dest + "/" + nsname);
			if (!d.Exists) d.Create();
			
			// Create the NS page
			string nsDest = opts.dest + "/" + nsname + "/index." + opts.ext;
			if (regenIndex) {
				overviewargs.AddParam("namespace", "", nsname);
				Generate(overview, overviewxsl, overviewargs, nsDest, template, sourceDirectories);
				overviewargs.RemoveParam("namespace", "");
			}
			
			foreach (XmlElement ty in ns.SelectNodes("Type")) {
				string typefilebase = ty.GetAttribute("Name");
				string sourceDir    = ty.GetAttribute("SourceDirectory");
				string typename = ty.GetAttribute("DisplayName");
				if (typename.Length == 0)
					typename = typefilebase;
				
				if (opts.onlytype != null && !(nsname + "." + typename).StartsWith(opts.onlytype))
					continue;

				string typefile = CombinePath (sourceDir, nsname, typefilebase + ".xml");
				if (typefile == null)
					continue;

				string destfile = opts.dest + "/" + nsname + "/" + typefilebase + "." + opts.ext;

				if (DestinationIsNewer (typefile, destfile))
					// target already exists, and is newer.  why regenerate?
					continue;

				XmlDocument typexml = new XmlDocument();
				typexml.Load(typefile);
				if (extensions != null) {
					DocLoader loader = CreateDocLoader (overview);
					XmlDocUtils.AddExtensionMethods (typexml, extensions, loader);
				}
				
				Console.WriteLine(nsname + "." + typename);
				
				Generate(typexml, stylesheet, typeargs, destfile, template, sourceDirectories);
			}
		}
	}

	private static ArrayList GetExtensionMethods (XmlDocument doc)
	{
		XmlNodeList extensions = doc.SelectNodes ("/Overview/ExtensionMethods/*");
		if (extensions.Count == 0)
			return null;
		ArrayList r = new ArrayList (extensions.Count);
		foreach (XmlNode n in extensions)
			r.Add (n);
		return r;
	}
	
	private static void DumpTemplate() {
		Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("defaulttemplate.xsl");
		Stream o = Console.OpenStandardOutput ();
		byte[] buf = new byte[1024];
		int r;
		while ((r = s.Read (buf, 0, buf.Length)) > 0) {
			o.Write (buf, 0, r);
		}
	}
	
	private static void Generate(XmlDocument source, XslTransform transform, XsltArgumentList args, string output, XslTransform template, List<string> sourceDirectories) {
		using (TextWriter textwriter = new StreamWriter(new FileStream(output, FileMode.Create))) {
			XmlTextWriter writer = new XmlTextWriter(textwriter);
			writer.Formatting = Formatting.Indented;
			writer.Indentation = 2;
			writer.IndentChar = ' ';
			
			try {
				XmlDocument intermediate = new XmlDocument();
				intermediate.PreserveWhitespace = true;
				intermediate.Load(transform.Transform(source, args, new ManifestResourceResolver(sourceDirectories.ToArray ()))); // FIXME?
				template.Transform(intermediate, new XsltArgumentList(), new XhtmlWriter (writer), null);
			} catch (Exception e) {
				throw new ApplicationException("An error occured while generating " + output, e);
			}
		}
	}
	
	private static XslTransform LoadTransform(string name, List<string> sourceDirectories) {
		try {
			XmlDocument xsl = new XmlDocument();
			xsl.Load(Assembly.GetExecutingAssembly().GetManifestResourceStream(name));
			
			if (name == "overview.xsl") {
				// bit of a hack.  overview needs the templates in stylesheet
				// for doc formatting, and rather than write a resolver, I'll
				// just do the import for it.
				
				XmlNode importnode = xsl.DocumentElement.SelectSingleNode("*[name()='xsl:include']");
				xsl.DocumentElement.RemoveChild(importnode);
				
				XmlDocument xsl2 = new XmlDocument();
				xsl2.Load(Assembly.GetExecutingAssembly().GetManifestResourceStream("stylesheet.xsl"));
				foreach (XmlNode node in xsl2.DocumentElement.ChildNodes)
					xsl.DocumentElement.AppendChild(xsl.ImportNode(node, true));
			}
			
			XslTransform t = new XslTransform();
			t.Load (xsl, new ManifestResourceResolver (sourceDirectories.ToArray ())); // FIXME?
			
			return t;
		} catch (Exception e) {
			throw new ApplicationException("Error loading " + name + " from internal resource", e);
		}
	}

	private static DocLoader CreateDocLoader (XmlDocument overview)
	{
		Hashtable docs = new Hashtable ();
		DocLoader loader = delegate (string s) {
			XmlDocument d = null;
			if (!docs.ContainsKey (s)) {
				foreach (XmlNode n in overview.SelectNodes ("//Type")) {
					string ns = n.ParentNode.Attributes ["Name"].Value;
					string t  = n.Attributes ["Name"].Value;
					string sd = n.Attributes ["SourceDirectory"].Value;
					if (s == ns + "." + t.Replace ("+", ".")) {
						string f = CombinePath (sd, ns, t + ".xml");
						if (File.Exists (f)) {
							d = new XmlDocument ();
							d.Load (f);
						}
						docs.Add (s, d);
						break;
					}
				}
			}
			else
				d = (XmlDocument) docs [s];
			return d;
		};
		return loader;
	}

	static string CombinePath (params string[] paths)
	{
		if (paths == null)
			return null;
		if (paths.Length == 1)
			return paths [0];
		var path = Path.Combine (paths [0], paths [1]);
		for (int i = 2; i < paths.Length; ++i)
			path = Path.Combine (path, paths [i]);
		return path;
	}

	private XmlDocument GetOverview (IEnumerable<string> directories)
	{
		var index = new XmlDocument ();

		var overview  = index.CreateElement ("Overview");
		var assemblies= index.CreateElement ("Assemblies");
		var types     = index.CreateElement ("Types");
		var ems       = index.CreateElement ("ExtensionMethods");

		index.AppendChild (overview);
		overview.AppendChild (assemblies);
		overview.AppendChild (types);
		overview.AppendChild (ems);

		bool first = true;

		foreach (var dir in directories) {
			var indexFile = Path.Combine (dir, "index.xml");
			try {
				var doc = new XmlDocument ();
				doc.Load (indexFile);
				if (first) {
					var c = doc.SelectSingleNode ("/Overview/Copyright");
					var t = doc.SelectSingleNode ("/Overview/Title");
					var r = doc.SelectSingleNode ("/Overview/Remarks");
					if (c != null && t != null && r != null) {
						var e = index.CreateElement ("Copyright");
						e.InnerXml = c.InnerXml;
						overview.AppendChild (e);

						e = index.CreateElement ("Title");
						e.InnerXml = t.InnerXml;
						overview.AppendChild (e);

						e = index.CreateElement ("Remarks");
						e.InnerXml = r.InnerXml;
						overview.AppendChild (e);

						first = false;
					}
				}
				AddAssemblies (assemblies, doc);
				AddTypes (types, doc, dir);
				AddChildren (ems, doc, "/Overview/ExtensionMethods");
			}
			catch (Exception e) {
				Message (TraceLevel.Warning, "Could not load documentation index '{0}': {1}",
						indexFile, e.Message);
			}
		}

		return index;
	}

	static void AddChildren (XmlNode dest, XmlDocument source, string path)
	{
		var n = source.SelectSingleNode (path);
		if (n != null)
			foreach (XmlNode c in n.ChildNodes)
				dest.AppendChild (dest.OwnerDocument.ImportNode (c, true));
	}

	static void AddAssemblies (XmlNode dest, XmlDocument source)
	{
		foreach (XmlNode asm in source.SelectNodes ("/Overview/Assemblies/Assembly")) {
			var n = asm.Attributes ["Name"].Value;
			var v = asm.Attributes ["Version"].Value;
			if (dest.SelectSingleNode (string.Format ("Assembly[@Name='{0}'][@Value='{1}']", n, v)) == null) {
				dest.AppendChild (dest.OwnerDocument.ImportNode (asm, true));
			}
		}
	}

	static void AddTypes (XmlNode dest, XmlDocument source, string sourceDirectory)
	{
		var types = source.SelectSingleNode ("/Overview/Types");
		if (types == null)
			return;
		foreach (XmlNode ns in types.ChildNodes) {
			var n = ns.Attributes ["Name"].Value;
			var nsd = dest.SelectSingleNode (string.Format ("Namespace[@Name='{0}']", n));
			if (nsd == null) {
				nsd = dest.OwnerDocument.CreateElement ("Namespace");
				AddAttribute (nsd, "Name", n);
				dest.AppendChild (nsd);
			}
			foreach (XmlNode t in ns.ChildNodes) {
				var c = dest.OwnerDocument.ImportNode (t, true);
				AddAttribute (c, "SourceDirectory", sourceDirectory);
				nsd.AppendChild (c);
			}
		}
	}

	static void AddAttribute (XmlNode self, string name, string value)
	{
		var a = self.OwnerDocument.CreateAttribute (name);
		a.Value = value;
		self.Attributes.Append (a);
	}

	private static bool DestinationIsNewer (string source, string dest)
	{
		return !opts.forceUpdate && File.Exists (dest) &&
			File.GetLastWriteTime (source) < File.GetLastWriteTime (dest);
	}
}

}
