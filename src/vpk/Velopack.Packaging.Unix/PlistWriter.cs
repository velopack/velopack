// https://raw.githubusercontent.com/egramtel/dotnet-bundle/master/DotNet.Bundle/PlistWriter.cs
using System.Xml;
using Microsoft.Extensions.Logging;
using Velopack.Core;
using Velopack.Util;

namespace Velopack.Packaging.Unix;

public class PlistWriter
{
    private readonly ILogger _logger;
    private readonly OsxAppInfo _task;
    private readonly string _outputDir;

    public const string PlistFileName = "Info.plist";

    public PlistWriter(ILogger logger, OsxAppInfo task, string outputDir)
    {
        _logger = logger;
        _task = task;
        _outputDir = outputDir;
    }

    public void Write()
    {
        var settings = new XmlWriterSettings {
            Indent = true,
            NewLineOnAttributes = false
        };

        var path = Path.Combine(_outputDir, PlistFileName);

        _logger.Debug($"Writing property list file: {path}");
        using (var xmlWriter = XmlWriter.Create(path, settings)) {
            xmlWriter.WriteStartDocument();

            xmlWriter.WriteRaw(Environment.NewLine);
            xmlWriter.WriteRaw(
                "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">");
            xmlWriter.WriteRaw(Environment.NewLine);

            xmlWriter.WriteStartElement("plist");
            xmlWriter.WriteAttributeString("version", "1.0");
            xmlWriter.WriteStartElement("dict");

            // if (!String.IsNullOrEmpty(_task.SQPackId))
            //     WriteProperty(xmlWriter, nameof(_task.SQPackId), _task.SQPackId);
            //
            // if (!String.IsNullOrEmpty(_task.SQPackAuthors))
            //     WriteProperty(xmlWriter, nameof(_task.SQPackAuthors), _task.SQPackAuthors);

            if (!String.IsNullOrEmpty(_task.CFBundleDisplayName))
                WriteProperty(xmlWriter, nameof(_task.CFBundleDisplayName), _task.CFBundleDisplayName);

            WriteProperty(xmlWriter, nameof(_task.CFBundleName), _task.CFBundleName);
            WriteProperty(xmlWriter, nameof(_task.CFBundleIdentifier), _task.CFBundleIdentifier);
            WriteProperty(xmlWriter, nameof(_task.CFBundleVersion), _task.CFBundleVersion);
            WriteProperty(xmlWriter, nameof(_task.CFBundlePackageType), _task.CFBundlePackageType);
            WriteProperty(xmlWriter, nameof(_task.CFBundleSignature), _task.CFBundleSignature);
            WriteProperty(xmlWriter, nameof(_task.CFBundleExecutable), _task.CFBundleExecutable);
            WriteProperty(xmlWriter, nameof(_task.CFBundleIconFile), Path.GetFileName(_task.CFBundleIconFile));
            WriteProperty(xmlWriter, nameof(_task.CFBundleShortVersionString), _task.CFBundleShortVersionString);
            WriteProperty(xmlWriter, nameof(_task.NSPrincipalClass), _task.NSPrincipalClass);
            WriteProperty(xmlWriter, nameof(_task.NSHighResolutionCapable), _task.NSHighResolutionCapable);

            if (_task.NSRequiresAquaSystemAppearance.HasValue) {
                WriteProperty(xmlWriter, nameof(_task.NSRequiresAquaSystemAppearance), _task.NSRequiresAquaSystemAppearance.Value);
            }

            if (_task.LSUIElement.HasValue) {
                WriteProperty(xmlWriter, nameof(_task.LSUIElement), _task.LSUIElement.Value);
            }

            WriteProperty(xmlWriter, nameof(_task.LSMinimumSystemVersion), _task.LSMinimumSystemVersion);
            WriteProperty(xmlWriter, nameof(_task.VelopackAssociationLaunchMode), _task.VelopackAssociationLaunchMode);

            WriteUrlTypes(xmlWriter);
            WriteDocumentTypes(xmlWriter);

            xmlWriter.WriteEndElement();
            xmlWriter.WriteEndElement();
        }
    }

    // CFBundleURLTypes = [ { CFBundleURLName, CFBundleTypeRole = Viewer, CFBundleURLSchemes = [...] } ]
    private void WriteUrlTypes(XmlWriter xmlWriter)
    {
        var schemes = _task.UrlSchemes ?? [];
        if (schemes.Length == 0) {
            return;
        }

        WriteKey(xmlWriter, "CFBundleURLTypes");
        xmlWriter.WriteStartElement("array");
        xmlWriter.WriteStartElement("dict");
        WriteProperty(xmlWriter, "CFBundleURLName", _task.CFBundleIdentifier);
        WriteProperty(xmlWriter, "CFBundleTypeRole", "Viewer");
        WriteProperty(xmlWriter, "CFBundleURLSchemes", schemes);
        xmlWriter.WriteEndElement(); // dict
        xmlWriter.WriteEndElement(); // array
    }

    // CFBundleDocumentTypes = per extension: { CFBundleTypeName, CFBundleTypeRole = Viewer, LSHandlerRank = Default,
    // CFBundleTypeExtensions = [ext] }. No UTI declarations (UTExportedTypeDeclarations) are generated.
    private void WriteDocumentTypes(XmlWriter xmlWriter)
    {
        var extensions = _task.DocumentExtensions ?? [];
        if (extensions.Length == 0) {
            return;
        }

        WriteKey(xmlWriter, "CFBundleDocumentTypes");
        xmlWriter.WriteStartElement("array");
        foreach (var ext in extensions) {
            var extNoDot = ext.TrimStart('.');
            xmlWriter.WriteStartElement("dict");
            WriteProperty(xmlWriter, "CFBundleTypeName", $"{_task.CFBundleName} {extNoDot.ToUpperInvariant()} File");
            WriteProperty(xmlWriter, "CFBundleTypeRole", "Viewer");
            WriteProperty(xmlWriter, "LSHandlerRank", "Default");
            WriteProperty(xmlWriter, "CFBundleTypeExtensions", [extNoDot]);
            xmlWriter.WriteEndElement(); // dict
        }

        xmlWriter.WriteEndElement(); // array
    }

    private static void WriteKey(XmlWriter xmlWriter, string name)
    {
        xmlWriter.WriteStartElement("key");
        xmlWriter.WriteString(name);
        xmlWriter.WriteEndElement();
    }

    private void WriteProperty(XmlWriter xmlWriter, string name, string value)
    {
        if (!string.IsNullOrWhiteSpace(value)) {
            WriteKey(xmlWriter, name);

            xmlWriter.WriteStartElement("string");
            xmlWriter.WriteString(value);
            xmlWriter.WriteEndElement();
        }
    }

    private void WriteProperty(XmlWriter xmlWriter, string name, bool value)
    {
        WriteKey(xmlWriter, name);

        if (value) {
            xmlWriter.WriteStartElement("true");
        } else {
            xmlWriter.WriteStartElement("false");
        }

        xmlWriter.WriteEndElement();
    }

    private void WriteProperty(XmlWriter xmlWriter, string name, string[] values)
    {
        if (values.Length != 0) {
            WriteKey(xmlWriter, name);

            xmlWriter.WriteStartElement("array");
            foreach (var value in values) {
                if (!string.IsNullOrEmpty(value)) {
                    xmlWriter.WriteStartElement("string");
                    xmlWriter.WriteString(value);
                    xmlWriter.WriteEndElement();
                }
            }

            xmlWriter.WriteEndElement();
        }
    }
}
