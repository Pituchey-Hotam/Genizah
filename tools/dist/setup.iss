; The offline installer: one version's folder, model included, installed for the current user
; without admin rights. It writes the same registry values the add-in's updater writes
; (Genizah/Registration.cs; the layout is in docs/RELEASING.md). Built by CI after package.ps1:
;   iscc /DVersion=1.2.3 tools\dist\setup.iss
; which reads tools\dist\out\version\ and writes tools\dist\out\Genizah-Setup-1.2.3.exe.

#ifndef Version
  #error Pass the version: iscc /DVersion=MAJOR.MINOR.PATCH setup.iss
#endif
#define Source "out\version"

; The public half of the key that signs the manifests: Genizah/Updater.cs's PublicKey.
#define PublicKey "<RSAKeyValue><Modulus>l+KhMOR15/7DRDaIDYDncJknBnBHJ52rXYN8fC53tVxjUh6Bq14yASSLZgpNKRQPx3KAPb0vMtvCjZjqycWIRupIWsE+KGVYWM9DRkCwfhUp0VXT2AYrrUwiI/MeZ5P3bVG4Q8F7hVn65pQj5iAG6rheJQ6JXBu6MlgX76KRodRQg3IQIBRZ8dxNJJTNiNLOujoKKVe0gSxcdnegrwvaRzYcQpqM4k0IBx315CrwLhGhzYCJNb13y09TS7yCYkpuV8z28RRneRy5M0guyWzVyk6ZcHXjVP4ML64zuwD9BUMUj2BhwSkmTWXLpJ5fDYoP1IedqG3uX/zWrMvFWbCmKMMtUeSjE0byLuvPNdtu9YbvIQd5bXHi3Xg3+2jHzCjRX2RzjrgkHsaz7sX8nbpTQs9LF2TzxkCib5GSxkX3gi0D9RyXHCWzycrca9w9pYCsgPxWeNcHecwsfY0zKbU0n3mxM/ooZSq18EgtmuzZL6GNlWJQSSaRprQCARBe3GiJ</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>"

[Setup]
AppId={{83AF029A-C5BF-4EC3-A4A4-5A4226B8C653}
AppName=Genizah
AppVerName=Genizah {#Version}
AppVersion={#Version}
AppPublisher=Genizah
AppPublisherURL=https://github.com/Pituchey-Hotam/Genizah
VersionInfoVersion={#Version}
DefaultDirName={localappdata}\Genizah
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
PrivilegesRequired=lowest
; Word is 64-bit; so is the add-in.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=out
OutputBaseFilename=Genizah-Setup-{#Version}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName=Genizah
; Files in use are the add-in's, which Word only reads at startup: no need to close it.
CloseApplications=no

[Languages]
Name: "he"; MessagesFile: "compiler:Languages\Hebrew.isl"

[CustomMessages]
he.NoVsto=לא נמצאה ההרחבה של Microsoft להרצת תוספים של Office (Visual Studio Tools for Office Runtime), שגניזה צריכה כדי לפעול.%n%nאפשר להתקין אותה מאתר Microsoft. להמשיך בכל זאת?

[Messages]
he.FinishedLabelNoIcons=גניזה הותקנה, ותיטען בהפעלה הבאה של Word.

[Files]
Source: "{#Source}\*"; DestDir: "{app}\versions\{#Version}"; Flags: recursesubdirs ignoreversion

[UninstallDelete]
; Versions the add-in installed itself, and the user's settings.
Type: filesandordirs; Name: "{app}"

[Code]
const
  AddInKey = 'Software\Microsoft\Office\Word\Addins\Genizah';
  InclusionKey = 'Software\Microsoft\VSTO\Security\Inclusion';

function InitializeSetup(): Boolean;
begin
  Result := True;
  if not FileExists(ExpandConstant('{commoncf64}\microsoft shared\VSTO\10.0\VSTOLoader.dll')) then
    Result := MsgBox(CustomMessage('NoVsto'), mbConfirmation, MB_YESNO) = IDYES;
end;

{ A file URL as the updater writes it (.NET's Uri.AbsoluteUri): '/' between folders, and
  anything but letters, digits and a few marks percent-encoded as UTF-8. }
function FileUrl(Path: String): String;
var
  I, C: Integer;
begin
  Result := 'file:///';
  for I := 1 to Length(Path) do
  begin
    C := Ord(Path[I]);
    if Path[I] = '\' then
      Result := Result + '/'
    else if ((C >= Ord('a')) and (C <= Ord('z'))) or ((C >= Ord('A')) and (C <= Ord('Z')))
         or ((C >= Ord('0')) and (C <= Ord('9'))) or (Pos(Path[I], '-._~:!$&''()*+,;=@') > 0) then
      Result := Result + Path[I]
    else if C < $80 then
      Result := Result + Format('%%%.2X', [C])
    else if C < $800 then
      Result := Result + Format('%%%.2X%%%.2X', [$C0 or (C shr 6), $80 or (C and $3F)])
    else
      Result := Result + Format('%%%.2X%%%.2X%%%.2X', [$E0 or (C shr 12), $80 or ((C shr 6) and $3F), $80 or (C and $3F)]);
  end;
end;

function NewGuid(): String;
var
  Bytes: String;
  I: Integer;
begin
  { Only a name for the inclusion entry: it needs to be unique, not random. }
  Bytes := GetSHA1OfString(ExpandConstant('{app}') + GetDateTimeString('yyyymmddhhnnsszzz', #0, #0));
  Result := '';
  for I := 1 to 32 do
  begin
    Result := Result + Bytes[I];
    if (I = 8) or (I = 12) or (I = 16) or (I = 20) then Result := Result + '-';
  end;
end;

{ Puts the version's manifest on VSTO's inclusion list, unless it is there already. }
procedure Trust(Url: String);
var
  Names: TArrayOfString;
  I: Integer;
  Existing, Entry: String;
begin
  if RegGetSubkeyNames(HKCU, InclusionKey, Names) then
    for I := 0 to GetArrayLength(Names) - 1 do
      if RegQueryStringValue(HKCU, InclusionKey + '\' + Names[I], 'Url', Existing)
         and (CompareText(Existing, Url) = 0)
         and RegQueryStringValue(HKCU, InclusionKey + '\' + Names[I], 'PublicKey', Existing)
         and (Existing = '{#PublicKey}') then
        exit;
  Entry := InclusionKey + '\' + NewGuid();
  RegWriteStringValue(HKCU, Entry, 'Url', Url);
  RegWriteStringValue(HKCU, Entry, 'PublicKey', '{#PublicKey}');
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Url: String;
begin
  if CurStep <> ssPostInstall then exit;
  Url := FileUrl(ExpandConstant('{app}\versions\{#Version}\Genizah.vsto'));
  Trust(Url);
  RegWriteStringValue(HKCU, AddInKey, 'Manifest', Url + '|vstolocal');
  RegWriteDWordValue(HKCU, AddInKey, 'LoadBehavior', 3);
  RegWriteStringValue(HKCU, AddInKey, 'FriendlyName', 'Genizah');
  RegWriteStringValue(HKCU, AddInKey, 'Description', 'Genizah');
end;

{ Removes the add-in's registration, and every inclusion entry for one of its version folders,
  whichever wrote it. }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Names: TArrayOfString;
  I: Integer;
  Url: String;
begin
  if CurUninstallStep <> usPostUninstall then exit;
  RegDeleteKeyIncludingSubkeys(HKCU, AddInKey);
  if RegGetSubkeyNames(HKCU, InclusionKey, Names) then
    for I := 0 to GetArrayLength(Names) - 1 do
      if RegQueryStringValue(HKCU, InclusionKey + '\' + Names[I], 'Url', Url)
         and (Pos('/genizah/versions/', Lowercase(Url)) > 0) then
        RegDeleteKeyIncludingSubkeys(HKCU, InclusionKey + '\' + Names[I]);
end;
