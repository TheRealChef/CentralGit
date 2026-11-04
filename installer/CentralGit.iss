; CentralGit Setup: the wizard people run. It wraps Velopack's Setup (which does the real
; install and owns updates/uninstall) and adds what Velopack doesn't have:
;   - fresh install: choose where to install
;   - already installed: "Installed X -> this setup Y", Update / Cancel, same location
; Inno itself installs nothing and registers no uninstaller; Velopack's entry
; (HKCU\...\Uninstall\CentralGit) stays the only one.
;
; Built by installer\build.ps1, which passes /DAppVersion, /DVelopackSetup, /DImageDir, /DIcon, /DOutputDir.

#ifndef AppVersion
  #error Run installer\build.ps1 instead of compiling this directly.
#endif

#define UninstallKey "Software\Microsoft\Windows\CurrentVersion\Uninstall\CentralGit"

[Setup]
AppId=CentralGit.Setup
AppName=CentralGit
AppVersion={#AppVersion}
AppVerName=CentralGit {#AppVersion}
AppPublisher=Hrvoje Kuhar
VersionInfoVersion={#AppVersion}
VersionInfoDescription=CentralGit Setup
DefaultDirName={localappdata}\CentralGit
AppendDefaultDirName=yes
DirExistsWarning=no
UsePreviousAppDir=no
DisableProgramGroupPage=yes
DisableWelcomePage=no
PrivilegesRequired=lowest
Uninstallable=no
CreateUninstallRegKey=no
WizardStyle=modern dark
WizardSizePercent=110
WizardImageFile={#ImageDir}\wizard-image.bmp
WizardSmallImageFile={#ImageDir}\wizard-small.bmp
WizardImageAlphaFormat=defined
SetupIconFile={#Icon}
; Velopack's Setup is already compressed.
Compression=none
OutputDir={#OutputDir}
OutputBaseFilename=CentralGit-Setup-{#AppVersion}

[Messages]
WelcomeLabel1=Install CentralGit {#AppVersion}
WelcomeLabel2=A fast, native Git client for Windows.%n%nCentralGit installs for your account only and doesn't need administrator rights. Your settings are kept in %APPDATA%\CentralGit and survive updates and uninstalls.
SelectDirDesc=Where should CentralGit be installed?
SelectDirLabel3=CentralGit will be installed in this folder. It needs about 250 MB.
ReadyLabel1=Ready to install CentralGit {#AppVersion}.
FinishedHeadingLabel=CentralGit is ready
FinishedLabelNoIcons=CentralGit {#AppVersion} is installed and starting. You'll find it in the Start menu.
FinishedLabel=CentralGit {#AppVersion} is installed and starting. You'll find it in the Start menu.

[Files]
Source: "{#VelopackSetup}"; DestName: "velopack-setup.exe"; Flags: dontcopy

[Code]
var
  InstalledVersion, InstalledDir: String;
  IsUpdate: Boolean;
  UpdatePage: TOutputMsgWizardPage;

{ Compares dotted versions numerically: <0, 0, >0. Pre-release suffixes are ignored. }
function CompareVersions(A, B: String): Integer;
var
  PA, PB: String;
  NA, NB, I: Integer;
begin
  Result := 0;
  A := A + '.'; B := B + '.';
  for I := 1 to 4 do
  begin
    PA := Copy(A, 1, Pos('.', A) - 1); Delete(A, 1, Pos('.', A));
    PB := Copy(B, 1, Pos('.', B) - 1); Delete(B, 1, Pos('.', B));
    if Pos('-', PA) > 0 then PA := Copy(PA, 1, Pos('-', PA) - 1);
    if Pos('-', PB) > 0 then PB := Copy(PB, 1, Pos('-', PB) - 1);
    NA := StrToIntDef(PA, 0); NB := StrToIntDef(PB, 0);
    if NA <> NB then
    begin
      if NA < NB then Result := -1 else Result := 1;
      Exit;
    end;
  end;
end;

function InitializeSetup(): Boolean;
begin
  IsUpdate := RegQueryStringValue(HKCU, '{#UninstallKey}', 'DisplayVersion', InstalledVersion)
          and RegQueryStringValue(HKCU, '{#UninstallKey}', 'InstallLocation', InstalledDir)
          and DirExists(InstalledDir);
  Result := True;
end;

procedure InitializeWizard();
var
  Cmp: Integer;
  Heading, Text: String;
begin
  if not IsUpdate then
    Exit;
  WizardForm.DirEdit.Text := InstalledDir;
  Cmp := CompareVersions('{#AppVersion}', InstalledVersion);
  if Cmp > 0 then
  begin
    Heading := 'Update CentralGit';
    Text := 'A newer version is ready to install.';
  end
  else if Cmp = 0 then
  begin
    Heading := 'Reinstall CentralGit';
    Text := 'This version is already installed. Reinstalling repairs the installation.';
  end
  else
  begin
    Heading := 'Install an older version?';
    Text := 'The installed version is newer than this setup. Continuing replaces it with the older one.';
  end;

  UpdatePage := CreateOutputMsgPage(wpWelcome, Heading, 'CentralGit is already installed on this PC.',
    'Installed version:   ' + InstalledVersion + #13#10 +
    'New version:          {#AppVersion}' + #13#10 +
    'Location:              ' + InstalledDir + #13#10#13#10 +
    Text + #13#10#13#10 +
    'Your settings and repositories are not touched. CentralGit closes during the update if it''s running.' + #13#10#13#10 +
    'Click Update to continue, or Cancel to keep things as they are.');
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  { An update goes straight from the version page to installing, into the existing folder. }
  Result := IsUpdate and ((PageID = wpWelcome) or (PageID = wpSelectDir) or (PageID = wpReady));
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if IsUpdate and (UpdatePage <> nil) and (CurPageID = UpdatePage.ID) then
    WizardForm.NextButton.Caption := 'Update';
end;

{ Refuse a fresh install into a folder that already holds other files: Velopack manages the
  whole install folder and would replace what's in it. }
function NextButtonClick(CurPageID: Integer): Boolean;
var
  Dir: String;
  Found: TFindRec;
  HasFiles: Boolean;
begin
  Result := True;
  if IsUpdate or (CurPageID <> wpSelectDir) then
    Exit;
  Dir := RemoveBackslashUnlessRoot(WizardDirValue);
  if Length(Dir) <= 3 then
  begin
    MsgBox('Please choose a folder, not the root of a drive.', mbError, MB_OK);
    Result := False;
    Exit;
  end;
  HasFiles := False;
  if FindFirst(Dir + '\*', Found) then
  try
    repeat
      if (Found.Name <> '.') and (Found.Name <> '..') then
        HasFiles := True;
    until HasFiles or not FindNext(Found);
  finally
    FindClose(Found);
  end;
  if HasFiles then
  begin
    MsgBox('"' + Dir + '" already contains files.' + #13#10#13#10 +
      'CentralGit manages its whole install folder, so please choose an empty or new folder.', mbError, MB_OK);
    Result := False;
  end;
end;

function TargetDir(): String;
begin
  if IsUpdate then
    Result := InstalledDir
  else
    Result := RemoveBackslashUnlessRoot(WizardDirValue);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Setup, Log, Params: String;
  Code: Integer;
begin
  if CurStep <> ssInstall then
    Exit;
  WizardForm.StatusLabel.Caption := 'Installing CentralGit {#AppVersion}...';
  WizardForm.FilenameLabel.Caption := TargetDir();
  WizardForm.ProgressGauge.Style := npbstMarquee;

  ExtractTemporaryFile('velopack-setup.exe');
  Setup := ExpandConstant('{tmp}\velopack-setup.exe');
  Log := AddBackslash(GetEnv('TEMP')) + 'CentralGit-setup.log';
  Params := '--silent --log "' + Log + '" --installto "' + TargetDir() + '"';

  if not Exec(Setup, Params, '', SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then
  begin
    WizardForm.ProgressGauge.Style := npbstNormal;
    MsgBox('CentralGit couldn''t be installed (exit code ' + IntToStr(Code) + ').' + #13#10#13#10 +
      'Details are in ' + Log, mbCriticalError, MB_OK);
    Abort;
  end;
  WizardForm.ProgressGauge.Style := npbstNormal;
end;
