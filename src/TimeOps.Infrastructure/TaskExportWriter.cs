using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using TimeOps.Application;
using TimeOps.Domain;

namespace TimeOps.Infrastructure;

public sealed class TaskExportWriter : ITaskExportWriter
{
    private const string SpreadsheetNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly string[] Headers = ["x", "Atividade", "OP/PMC/PGP", "Nome da OP/PMC/PGP", "Data", "Horas", "Comentários"];
    private static readonly double[] Widths = [30, 64, 64, 28, 14, 8, 30];

    public Result<byte[]> Write(IReadOnlyList<TaskExportRow> rows)
    {
        if (rows.Count > 1_048_575)
            return Result<byte[]>.Failure(new("export.size", ErrorCategory.Validation, "O Excel comporta até 1.048.575 alterações. Reduza o período."));
        try
        {
            using var output = new MemoryStream();
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                AddXml(archive, "[Content_Types].xml", """
                    <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                      <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                      <Default Extension="xml" ContentType="application/xml"/>
                      <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                      <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
                      <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                    </Types>
                    """);
                AddXml(archive, "_rels/.rels", """
                    <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                      <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                    </Relationships>
                    """);
                AddXml(archive, "xl/workbook.xml", """
                    <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                      <sheets><sheet name="Tarefas" sheetId="1" r:id="rId1"/></sheets>
                    </workbook>
                    """);
                AddXml(archive, "xl/_rels/workbook.xml.rels", """
                    <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                      <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                      <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
                    </Relationships>
                    """);
                AddXml(archive, "xl/styles.xml", """
                    <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                      <numFmts count="2"><numFmt numFmtId="164" formatCode="dd/mm/yyyy"/><numFmt numFmtId="165" formatCode="0.##"/></numFmts>
                      <fonts count="2">
                        <font><sz val="10"/><color rgb="FF000000"/><name val="Arial"/></font>
                        <font><b/><sz val="10"/><color rgb="FF000000"/><name val="Arial"/></font>
                      </fonts>
                      <fills count="3"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill><fill><patternFill patternType="solid"><fgColor rgb="FF808080"/><bgColor indexed="64"/></patternFill></fill></fills>
                      <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
                      <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
                      <cellXfs count="4">
                        <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
                        <xf numFmtId="0" fontId="1" fillId="2" borderId="0" xfId="0" applyFont="1" applyFill="1" applyAlignment="1"><alignment horizontal="center" vertical="center"/></xf>
                        <xf numFmtId="164" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1" applyAlignment="1"><alignment horizontal="center"/></xf>
                        <xf numFmtId="165" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1" applyAlignment="1"><alignment horizontal="center"/></xf>
                      </cellXfs>
                      <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
                    </styleSheet>
                    """);
                using var sheetStream = archive.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.Fastest).Open();
                using var xml = XmlWriter.Create(sheetStream, new() { Encoding = new UTF8Encoding(false), CloseOutput = false });
                xml.WriteStartDocument();
                xml.WriteStartElement("worksheet", SpreadsheetNamespace);
                xml.WriteStartElement("dimension");
                xml.WriteAttributeString("ref", $"A1:G{rows.Count + 1}");
                xml.WriteEndElement();
                xml.WriteStartElement("sheetViews");
                xml.WriteStartElement("sheetView");
                xml.WriteAttributeString("workbookViewId", "0");
                xml.WriteAttributeString("showGridLines", "1");
                xml.WriteEndElement();
                xml.WriteEndElement();
                xml.WriteStartElement("sheetFormatPr");
                xml.WriteAttributeString("defaultRowHeight", "15");
                xml.WriteEndElement();
                xml.WriteStartElement("cols");
                for (var index = 0; index < Widths.Length; index++)
                {
                    xml.WriteStartElement("col");
                    xml.WriteAttributeString("min", (index + 1).ToString(CultureInfo.InvariantCulture));
                    xml.WriteAttributeString("max", (index + 1).ToString(CultureInfo.InvariantCulture));
                    xml.WriteAttributeString("width", Widths[index].ToString(CultureInfo.InvariantCulture));
                    xml.WriteAttributeString("customWidth", "1");
                    xml.WriteEndElement();
                }
                xml.WriteEndElement();
                xml.WriteStartElement("sheetData");
                xml.WriteStartElement("row");
                xml.WriteAttributeString("r", "1");
                for (var index = 0; index < Headers.Length; index++) TextCell(xml, $"{(char)('A' + index)}1", Headers[index], "1");
                xml.WriteEndElement();
                for (var index = 0; index < rows.Count; index++)
                {
                    var row = rows[index];
                    var number = index + 2;
                    xml.WriteStartElement("row");
                    xml.WriteAttributeString("r", number.ToString(CultureInfo.InvariantCulture));
                    TextCell(xml, $"A{number}", row.AuthorName);
                    TextCell(xml, $"B{number}", row.TaskTitle);
                    TextCell(xml, $"C{number}", row.FeatureTitle ?? "");
                    TextCell(xml, $"D{number}", "");
                    NumberCell(xml, $"E{number}", row.Date.ToDateTime(TimeOnly.MinValue).ToOADate().ToString(CultureInfo.InvariantCulture), "2");
                    NumberCell(xml, $"F{number}", row.Hours.ToString(CultureInfo.InvariantCulture), "3");
                    TextCell(xml, $"G{number}", "");
                    xml.WriteEndElement();
                }
                xml.WriteEndElement();
                xml.WriteEndElement();
                xml.WriteEndDocument();
            }
            return Result<byte[]>.Success(output.ToArray());
        }
        catch (Exception exception) when (exception is ArgumentException or XmlException or IOException)
        {
            return Result<byte[]>.Failure(new("export.write", ErrorCategory.Unexpected,
                "Não foi possível gerar o Excel. Verifique os nomes das tarefas e tente novamente."));
        }
    }

    private static void AddXml(ZipArchive archive, string name, string content)
    {
        using var stream = archive.CreateEntry(name, CompressionLevel.Fastest).Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(content);
    }

    private static void TextCell(XmlWriter xml, string reference, string value, string style = "0")
    {
        if (value.Length > 32_767) throw new ArgumentException("Texto excede o limite de uma célula do Excel.");
        xml.WriteStartElement("c");
        xml.WriteAttributeString("r", reference);
        xml.WriteAttributeString("s", style);
        // Inline strings keep user-controlled titles literal, including an initial '='.
        xml.WriteAttributeString("t", "inlineStr");
        xml.WriteStartElement("is");
        xml.WriteStartElement("t");
        xml.WriteAttributeString("xml", "space", "http://www.w3.org/XML/1998/namespace", "preserve");
        xml.WriteString(value);
        xml.WriteEndElement();
        xml.WriteEndElement();
        xml.WriteEndElement();
    }

    private static void NumberCell(XmlWriter xml, string reference, string value, string style)
    {
        xml.WriteStartElement("c");
        xml.WriteAttributeString("r", reference);
        xml.WriteAttributeString("s", style);
        xml.WriteElementString("v", value);
        xml.WriteEndElement();
    }
}
