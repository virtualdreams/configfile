<#
	.SYNOPSIS
	
	This script generate a version number from a git repository.
		
	.DESCRIPTION
	
	This script a generate version number from a git repository. Major and minor
	version number are created from a tag if in form of 'v1.0-release...'.
	Otherwise, the version numbers are extracted from file.
	
	Git 2.x for Windows (and the correct installed architecture) is required to work.
	https://git-scm.com/download/win
	
	.PARAMETER file
	
	Path to 'AssemblyInfo.cs'
	
	.PARAMETER git
	
	Path to 'git.exe'
	
	.EXAMPLE
	
	powershell -file ".\version.ps1" -file "/path/to/AssemblyInfo.cs" [-git "/path/to/git"]
	
	.NOTES
	
	Author: Thomas Kindler
	Date:	29.02.2015
	
	.LINK
	
	github: https://gist.github.com/virtualdreams/23004251c9ab4f2d04db
#>
param
(
	[string]$file = '.\Properties\AssemblyInfo.cs',
	[string]$git = 'C:\Program Files\git\bin\git.exe'
)

### test if the git executable exists
if(!(Test-Path $git))
{
	Write-Host "Git command could not be found."
	return
}

### set git exe as alias
Set-Alias git $git
if(!(Get-Command git -TotalCount 1 -ErrorAction SilentlyContinue))
{
	Write-Host "Git could not be found."
	return
}

### test if target file exists
if(!$file -or !(Test-Path $file))
{
	Write-Host "The path to 'AssemblyInfo.cs' is not set or not found."
	return
}

### Get file version from AssemblyInfo.cs
function Get-FileVersion()
{
	$hash = @{
		Major = 1
		Minor = 0
		Revision = 0
		Build = 0
		Parsed = $false
	}
	$obj = New-Object PSObject -Property $hash

	### select string from assembly file
	$info = Select-String -Path $file -Pattern "\[assembly: AssemblyFileVersion\(""(\d+)\.(\d+)\.(\d+)\.(\d+)\""\)\]"  | % { $_.Matches } | % { $_.Value } 
	if($info -match "\[assembly: AssemblyFileVersion\(""(\d+)\.(\d+)\.(\d+)\.(\d+)\""\)\]")
	{
		$major = [string]$matches[1]
		$minor = [string]$matches[2]
		$build = [string]$matches[3]
		$revision = [string]$matches[4]
		
		
		$obj.Major = $major
		$obj.Minor = $minor
		$obj.Revision = $revision
		$obj.Build = $build
		$obj.Parsed = $true
	}
	
	return $obj
}

### Get git version information from tag.
### Example: v1.0-release-0-g0000000
function Get-GitVersion()
{
	$hash = @{
		Major = 1
		Minor = 0
		Revision = 0
		Build = 0
		Parsed = $false
	}
	$obj = New-Object PSObject -Property $hash
	
	### get total commit count
	$commits = git rev-list HEAD --count

	### get a description
	$info = git describe --long --always --dirty=-dev

	if($info -match "^v(\d+).(\d+)-.*-(\d+)-g.*$")
	{
		$major = [string]$matches[1]
		$minor = [string]$matches[2]
		$revision = [string]$matches[3]
		
		$obj.Major = $major
		$obj.Minor = $minor
		$obj.Build = $commits
		$obj.Revision = $revision
		$obj.Parsed = $true
	}
	
	return $obj
}

### Get description string
function Get-GitDescription()
{
	$info = git describe --long --always --dirty=-dev
	return $info
}

### Write version info to AssemblyInfo.cs
function Write-Assembly([string]$major, [string]$minor, [string]$build, [string]$revision, [string]$description)
{
	(Get-Content $file -Encoding UTF8) `
		-replace "^\[assembly: AssemblyVersion\("".*""\)\]", "[assembly: AssemblyVersion(""$major.0.0.0"")]" `
		-replace "^\[assembly: AssemblyFileVersion\("".*""\)\]", "[assembly: AssemblyFileVersion(""$major.$minor.$build.$revision"")]" `
		-replace "^\[assembly: AssemblyInformationalVersion\(""(.*)""\)\]", @("[assembly: AssemblyInformationalVersion(""$description"")]"; "[assembly: AssemblyInformationalVersion(""`$1"")]")[[string]::IsNullOrEmpty($description) -eq $True] |    #"[assembly: AssemblyInformationalVersion(""$description"")]" |
	Out-File $file -Encoding UTF8
}

Write-Host "Get version information..."

$d = Get-GitDescription
$g = Get-GitVersion
$a = Get-FileVersion

Write-Host "Git:          " $g
Write-Host "Assembly:     " $a
Write-Host "Description:  " $d

if($g.Parsed -eq $True)
{
	Write-Host "Use from git: " $g.Major $g.Minor $g.Build $g.Revision $d
	Write-Assembly $g.Major $g.Minor $g.Build $g.Revision $d
}
else
{
	Write-Host "Use from file:" $a.Major $a.Minor $a.Build $a.Revision $d
	Write-Assembly $a.Major $a.Minor $a.Build $a.Revision $d
}

Write-Host "Version information updated."
