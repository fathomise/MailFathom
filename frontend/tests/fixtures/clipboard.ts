// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

// What a clipboard carries when somebody copies from a web page, a spreadsheet, or a page written to attack the
// composer, recorded in the shapes each source writes and filled with made-up words and figures. The unit suite reads
// them through the composer's reader, the browser suite pastes and drops them on Chromium, and the desktop suite in
// the shell's own WebView. Every host is a reserved `.invalid` name, so nothing here can reach a machine that exists.
//
// Each document is written a line to an entry, because what it holds is markup and style sheets rather than an answer
// of the deployment's, and the corpus contract reads a line at a time.

/** The global a script or a handler in `hostileDocument` would set, which stays unset while nothing in it ran. */
export const ranMarker = 'clipboardFixtureRan';

/** A document carrying every construct ADR 0047 Question 1 lists as something a paste must neither run nor fetch. */
export const hostileDocument = [
    `<html><head>`,
    `<base href="https://base.example.invalid/">`,
    `<meta http-equiv="refresh" content="0;url=https://refresh.example.invalid/">`,
    `<link rel="stylesheet" href="https://stylesheet.example.invalid/sheet.css">`,
    `<link rel="preload" as="image" href="https://preload.example.invalid/a.png">`,
    `<link rel="prefetch" href="https://prefetch.example.invalid/b">`,
    `<link rel="dns-prefetch" href="https://dns-prefetch.example.invalid">`,
    `<link rel="icon" href="https://icon.example.invalid/i.png">`,
    `<style>@import url("https://import.example.invalid/x.css"); p { background: url("https://sheet-url.example.invalid/bg.png"); }</style>`,
    `<script>globalThis.clipboardFixtureRan = "script";</script>`,
    `</head><body background="https://body.example.invalid/bg.png" onload="globalThis.clipboardFixtureRan = 'onload'">`,
    `<p onclick="globalThis.clipboardFixtureRan = 'onclick'" style="background-image: url('https://inline-url.example.invalid/x.png')">Quarterly <a href="javascript:globalThis.clipboardFixtureRan = 'link'">notes</a></p>`,
    `<svg onload="globalThis.clipboardFixtureRan = 'svg'"><circle r="1"></circle></svg>`,
    `<img src="https://img.example.invalid/a.png" srcset="https://srcset.example.invalid/a.png 2x" onerror="globalThis.clipboardFixtureRan = 'onerror'">`,
    `<picture><source srcset="https://source.example.invalid/b.webp"><img src="https://picture.example.invalid/b.png"></picture>`,
    `<video poster="https://poster.example.invalid/c.png"></video>`,
    `<object data="https://object.example.invalid/d"></object>`,
    `<embed src="https://embed.example.invalid/e">`,
    `<iframe src="https://frame.example.invalid/f"></iframe>`,
    `<img src="file:///C:/Users/sample/clip.png"><img src="cid:part1.sample@example.invalid">`,
    `<table background="https://table.example.invalid/t.png"><tr><td background="https://cell.example.invalid/c.png">Kept</td><td>words</td></tr></table>`,
    `</body></html>`,
].join('\n');

/** A passage copied from a web page: headings, emphasis, colour, size, lists, a quote, links, and a remote picture. */
export const formattedBlock = [
    `<meta charset="utf-8"><h1>Yard handover</h1>`,
    `<p>The racking is <strong>ready</strong>, the <em>north</em> bay is <u>marked</u>, and the old plan is <s>void</s>.</p>`,
    `<p><span style="color: rgb(180, 35, 24);">Gate code changes Friday.</span> <span style="color: #222222;">Ask the desk</span> <span style="background-color: #fef0c7;">today</span>.</p>`,
    `<ul><li>Bay 4</li><li style="text-align: center;">Bay 5</li></ul>`,
    `<ol><li>Sweep</li></ol>`,
    `<blockquote>Keep the fire lane clear.</blockquote>`,
    `<p>See <a href="https://yard.example.invalid/plan">the plan</a> or <a href="javascript:void(0)">nothing</a>.</p>`,
    `<h2>Contacts</h2>`,
    `<h4>Desk</h4>`,
    `<p style="font-size: 9pt;">Small print</p>`,
    `<p><font color="#067647" size="5">Green and larger</font></p>`,
    `<img src="https://pictures.example.invalid/yard.png" alt="The yard">`,
].join('\n');

/** The plain text a browser writes beside `formattedBlock`. */
export const formattedBlockText = [
    'Yard handover',
    'The racking is ready, the north bay is marked, and the old plan is void.',
    'Gate code changes Friday. Ask the desk today.',
    'Bay 4',
    'Bay 5',
    'Sweep',
    'Keep the fire lane clear.',
    'See the plan or nothing.',
    'Contacts',
    'Desk',
    'Small print',
    'Green and larger',
].join('\n');

/** A range copied from Excel: a bold, filled header row by class, right-aligned figures, a red one, an italic cell. */
export const excelRange = [
    `<html xmlns:o="urn:schemas-microsoft-com:office:office" xmlns:x="urn:schemas-microsoft-com:office:excel" xmlns="http://www.w3.org/TR/REC-html40">`,
    `<head><meta http-equiv=Content-Type content="text/html; charset=utf-8"><meta name=ProgId content=Excel.Sheet><meta name=Generator content="Microsoft Excel 15">`,
    `<link id=Main-File rel=Main-File href="file:///C:/Users/sample/AppData/Local/Temp/msohtmlclip1/01/clip.htm">`,
    `<style>`,
    `<!--table`,
    `	{mso-displayed-decimal-separator:"\\.";`,
    `	mso-displayed-thousand-separator:"\\,";}`,
    `@page`,
    `	{margin:.75in .7in .75in .7in;}`,
    `tr`,
    `	{mso-height-source:auto;}`,
    `br`,
    `	{mso-data-placement:same-cell;}`,
    `td`,
    `	{padding-top:1px;`,
    `	mso-ignore:padding;`,
    `	color:black;`,
    `	font-size:11.0pt;`,
    `	font-weight:400;`,
    `	font-style:normal;`,
    `	text-decoration:none;`,
    `	font-family:Calibri, sans-serif;`,
    `	mso-number-format:General;`,
    `	text-align:general;`,
    `	vertical-align:bottom;`,
    `	border:none;`,
    `	white-space:nowrap;}`,
    `.xl65`,
    `	{font-weight:700;`,
    `	background:#D9E1F2;`,
    `	mso-pattern:black none;}`,
    `.xl66`,
    `	{mso-number-format:"\\#\\,\\#\\#0";}`,
    `.xl67`,
    `	{color:#C00000;`,
    `	mso-number-format:"\\#\\,\\#\\#0";}`,
    `.xl68`,
    `	{font-style:italic;}`,
    `-->`,
    `</style></head>`,
    `<body link="#0563C1" vlink="#954F72">`,
    `<table border=0 cellpadding=0 cellspacing=0 width=320 style='border-collapse:collapse;width:240pt'>`,
    `<!--StartFragment-->`,
    ` <col width=128 style='width:96pt'>`,
    ` <col width=64 span=3 style='width:48pt'>`,
    ` <tr height=20 style='height:15.0pt'>`,
    `  <td height=20 class=xl65 width=128 style='height:15.0pt;width:96pt'>Region</td>`,
    `  <td class=xl65 width=64 style='width:48pt'>Units</td>`,
    `  <td class=xl65 width=64 style='width:48pt'>Change</td>`,
    `  <td width=64 style='width:48pt'></td>`,
    ` </tr>`,
    ` <tr height=20 style='height:15.0pt'>`,
    `  <td height=20 style='height:15.0pt'>North yard</td>`,
    `  <td class=xl66 align=right>1,240</td>`,
    `  <td class=xl67 align=right>-35</td>`,
    `  <td></td>`,
    ` </tr>`,
    ` <tr height=20 style='height:15.0pt'>`,
    `  <td height=20 class=xl68 style='height:15.0pt'>South yard</td>`,
    `  <td class=xl66 align=right>980</td>`,
    `  <td class=xl66 align=right>12</td>`,
    `  <td></td>`,
    ` </tr>`,
    ` <tr height=20 style='height:15.0pt'>`,
    `  <td height=20 style='height:15.0pt'></td>`,
    `  <td></td>`,
    `  <td></td>`,
    `  <td></td>`,
    ` </tr>`,
    `<!--EndFragment-->`,
    `</table>`,
    `</body>`,
    `</html>`,
].join('\n');

/** A range copied from LibreOffice Calc: a header row bold by `<b>` and filled by `bgcolor`, a red figure by `<font>`. */
export const calcRange = [
    `<!DOCTYPE HTML PUBLIC "-//W3C//DTD HTML 4.0 Transitional//EN">`,
    `<html><head><meta http-equiv="content-type" content="text/html; charset=utf-8"/><title></title><meta name="generator" content="LibreOffice 24.2.7.2 (Linux)"/>`,
    `<style type="text/css">`,
    `	body,div,table,thead,tbody,tfoot,tr,th,td,p { font-family:"Liberation Sans"; font-size:x-small }`,
    `	a.comment-indicator:hover + comment { background:#ffd; position:absolute; display:block; border:1px solid black; padding:0.5em; }`,
    `	comment { display:none; }`,
    `</style></head>`,
    `<body>`,
    `<table cellspacing="0" border="0">`,
    `	<colgroup width="120"></colgroup>`,
    `	<colgroup span="2" width="85"></colgroup>`,
    `	<tr>`,
    `		<td height="17" align="left" bgcolor="#DEE6EF"><b>Region</b></td>`,
    `		<td align="left" bgcolor="#DEE6EF"><b>Units</b></td>`,
    `		<td align="left" bgcolor="#DEE6EF"><b>Change</b></td>`,
    `	</tr>`,
    `	<tr>`,
    `		<td height="17" align="left">North yard</td>`,
    `		<td align="right" sdval="1240" sdnum="1033;0;#,##0">1,240</td>`,
    `		<td align="right" sdval="-35" sdnum="1033;0;#,##0"><font color="#C9211E">-35</font></td>`,
    `	</tr>`,
    `	<tr>`,
    `		<td height="17" align="left"><i>South yard</i></td>`,
    `		<td align="right" sdval="980" sdnum="1033;0;#,##0">980</td>`,
    `		<td align="right" sdval="12" sdnum="1033;0;#,##0">12</td>`,
    `	</tr>`,
    `</table>`,
    `</body>`,
    `</html>`,
].join('\n');

/** A range copied from Google Sheets: every cell styled inline, the header bold and filled, a red figure. */
export const sheetsRange = [
    `<meta charset="utf-8"><google-sheets-html-origin><style type="text/css"><!--td {border: 1px solid #cccccc;}br {mso-data-placement:same-cell;}--></style>`,
    `<table xmlns="http://www.w3.org/1999/xhtml" cellspacing="0" cellpadding="0" dir="ltr" border="1" style="table-layout:fixed;font-size:10pt;font-family:Arial;width:0px;border-collapse:collapse;border:none" data-sheets-root="1"><colgroup><col width="100"/><col width="100"/><col width="100"/></colgroup><tbody>`,
    `<tr style="height:21px;"><td style="overflow:hidden;padding:2px 3px 2px 3px;vertical-align:bottom;background-color:#fff2cc;font-weight:bold;">Region</td><td style="overflow:hidden;padding:2px 3px 2px 3px;vertical-align:bottom;background-color:#fff2cc;font-weight:bold;">Units</td><td style="overflow:hidden;padding:2px 3px 2px 3px;vertical-align:bottom;background-color:#fff2cc;font-weight:bold;">Change</td></tr>`,
    `<tr style="height:21px;"><td style="overflow:hidden;padding:2px 3px 2px 3px;vertical-align:bottom;">North yard</td><td style="overflow:hidden;padding:2px 3px 2px 3px;vertical-align:bottom;text-align:right;" data-sheets-value="{&quot;1&quot;:3,&quot;3&quot;:1240}">1,240</td><td style="overflow:hidden;padding:2px 3px 2px 3px;vertical-align:bottom;color:#ff0000;text-align:right;" data-sheets-value="{&quot;1&quot;:3,&quot;3&quot;:-35}">-35</td></tr>`,
    `<tr style="height:21px;"><td style="overflow:hidden;padding:2px 3px 2px 3px;vertical-align:bottom;font-style:italic;">South yard</td><td style="overflow:hidden;padding:2px 3px 2px 3px;vertical-align:bottom;text-align:right;" data-sheets-value="{&quot;1&quot;:3,&quot;3&quot;:980}">980</td><td style="overflow:hidden;padding:2px 3px 2px 3px;vertical-align:bottom;text-align:right;" data-sheets-value="{&quot;1&quot;:3,&quot;3&quot;:12}">12</td></tr>`,
    `</tbody></table></google-sheets-html-origin>`,
].join('\n');

/** The tab-separated text every spreadsheet above writes beside its markup. */
export const rangeText = ['Region\tUnits\tChange', 'North yard\t1,240\t-35', 'South yard\t980\t12', ''].join('\n');

/** A table holding a table in one of its cells, which the composer holds as the inner table's words. */
export const nestedTable = [
    `<table><tr><td>Bay</td><td><table><tr><td>Shelf A</td><td>Shelf B</td></tr></table></td></tr>`,
    `<tr><td>4</td><td>Empty</td></tr></table>`,
].join('\n');
