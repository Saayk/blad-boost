# Lista los textos en español que no tienen traducción en Lang.cs
$dir = $PSScriptRoot
$lang = Get-Content "$dir\Lang.cs" -Raw -Encoding UTF8
$keys = New-Object 'System.Collections.Generic.HashSet[string]'
foreach ($m in [regex]::Matches($lang, '\{\s*"((?:[^"\\]|\\.)*)"\s*,\s*"')) { [void]$keys.Add($m.Groups[1].Value) }

$used = New-Object 'System.Collections.Generic.HashSet[string]'
foreach ($f in 'App.cs', 'Services.cs', 'Setup.cs') {
  $t = Get-Content "$dir\$f" -Raw -Encoding UTF8
  foreach ($m in [regex]::Matches($t, '(?:\bTr|\bTf|L\.T|L\.F)\(\s*"((?:[^"\\]|\\.)*)"')) { [void]$used.Add($m.Groups[1].Value) }
}
# títulos de métricas del benchmark y categorías / items del núcleo
$app = Get-Content "$dir\App.cs" -Raw -Encoding UTF8
foreach ($m in [regex]::Matches($app, 'Title = "((?:[^"\\]|\\.)*)"')) { [void]$used.Add($m.Groups[1].Value) }
$core = Get-Content "$dir\Core.cs" -Raw -Encoding UTF8
foreach ($m in [regex]::Matches($core, 'Add\("\w+", \w+, "((?:[^"\\]|\\.)*)",\s*"((?:[^"\\]|\\.)*)"')) { [void]$used.Add($m.Groups[1].Value); [void]$used.Add($m.Groups[2].Value) }
foreach ($m in [regex]::Matches($core, 'Add\("\w+", "((?:[^"\\]|\\.)*)", "((?:[^"\\]|\\.)*)"')) { [void]$used.Add($m.Groups[1].Value); [void]$used.Add($m.Groups[2].Value) }
foreach ($m in [regex]::Matches($core, 'Cats = \{([^}]*)\}')) { foreach ($c in [regex]::Matches($m.Groups[1].Value, '"([^"]+)"')) { [void]$used.Add($c.Groups[1].Value) } }
foreach ($w in 'Portátil', 'Sobremesa') { [void]$used.Add($w) }
foreach ($x in 'MainWindow.xaml', 'Setup.xaml') {
  $t = Get-Content "$dir\$x" -Raw -Encoding UTF8
  foreach ($m in [regex]::Matches($t, '\b(?:Text|Content|Title)="([^"{&][^"]*)"')) { [void]$used.Add($m.Groups[1].Value.Replace('&amp;', '&')) }
}
$missing = $used | ? { ($_ -match '\p{L}') -and -not $keys.Contains($_) } | Sort
"used=$($used.Count) keys=$($keys.Count) missing=$($missing.Count)"
$missing
