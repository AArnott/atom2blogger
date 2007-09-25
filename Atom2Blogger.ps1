param ($username, $password, $uri)

function Get-Service($username, $password) {
	[void][reflection.assembly]::LoadFile("$env:ProgramFiles\Google\Google Data API SDK\Redist\Google.GData.Client.dll")
	$svc = new-object Google.GData.Client.Service("blogger","atom2blogger")
	$svc.credentials = new-object net.networkcredential($username, $password)
	$svc
}

function Get-AtomFeed($service, $uri, $numberToRetrieve=100) {
	$query = New-Object Google.GData.Client.FeedQuery($uri)
	$query.NumberToRetrieve = $numberToRetrieve
	$service.Query($query)
}

$svc = Get-Service $username $password
$feed = Get-AtomFeed $svc $uri
