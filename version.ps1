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
	[string]$git = 'C:\Program Files\git\bin\git.exe',
	[bool]$usefile = $false
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

### Get description string
function Get-GitDescription()
{
	[string]$info = git describe --long --always --dirty=-dev 2> $null
	return $info
}

### Get file version from AssemblyInfo.cs
function Get-FileVersion()
{
	$obj = New-Object PSObject -Property @{
		Major = 1
		Minor = 0
		Revision = 0
		Build = 0
		Parsed = $false
	}

	### select string from assembly file
	$info = Select-String -Path $file -Pattern "\[assembly: AssemblyFileVersion\(""(\d+)\.(\d+)\.(\d+)\.(\d+)\""\)\]"  | % { $_.Matches } | % { $_.Value } 
	if($info -match "\[assembly: AssemblyFileVersion\(""(\d+)\.(\d+)\.(\d+)\.(\d+)\""\)\]")
	{
		$major = [int]$matches[1]
		$minor = [int]$matches[2]
		$build = [int]$matches[3]
		$revision = [int]$matches[4]
		
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
	$obj = New-Object PSObject -Property @{
		Major = 1
		Minor = 0
		Revision = 0
		Build = 0
		Parsed = $false
	}
	
	### get total commit count
	[int]$commits = git rev-list HEAD --count 2> $null

	### get a description
	[string]$info = Get-GitDescription
	
	### get version number from description
	if($info -match "^v(\d+).(\d+)-.*-(\d+)-g.*$")
	{
		$major = [int]$matches[1]
		$minor = [int]$matches[2]
		$revision = [int]$matches[3]
		
		$obj.Major = $major
		$obj.Minor = $minor
		$obj.Revision = $revision
	}
	
	$obj.Parsed = $commits -gt 0
	$obj.Build = $commits
	
	return $obj
}

### Write version info to AssemblyInfo.cs
function Write-Assembly([int]$major, [int]$minor, [int]$build, [int]$revision, [string]$description)
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

if ( $usefile -eq $false -and $g.Parsed )
{
	Write-Host "Use from git: " $g.Major $g.Minor $g.Build $g.Revision $d
	Write-Assembly $g.Major $g.Minor $g.Build $g.Revision $d
	Write-Host "Version information updated."
}
elseif ( $a.Parsed -eq $true )
{
	Write-Host "Use from file:" $a.Major $a.Minor $a.Build $a.Revision ""
	Write-Assembly $a.Major $a.Minor $a.Build $a.Revision ""
	Write-Host "Version information updated."
}
else
{
	Write-Host "No version information updated."
}