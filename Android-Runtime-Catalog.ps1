# Reads Addressables catalog metadata only. Does not load or execute game code.
function Read-BapInt32 {
    param([byte[]]$Data,[int]$Offset)
    if ($Offset -lt 0 -or $Offset -gt $Data.Length-4) { throw 'Catalog offset outside data.' }
    return [BitConverter]::ToInt32($Data,$Offset)
}
function Get-BapRuntimeCatalog {
    param([string]$Path)
    $info=Get-Item -LiteralPath $Path
    if ($info.Length -le 0 -or $info.Length -gt 64MB) { throw 'Invalid catalog size.' }
    $catalog=Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($catalog.m_LocatorId -ne 'AddressablesMainContentCatalog') { throw 'Unsupported catalog locator.' }
    if (@($catalog.m_InternalIdPrefixes).Count -ne 0) { throw 'Catalog internal ID prefixes require review.' }
    $keys=[Convert]::FromBase64String($catalog.m_KeyDataString)
    $buckets=[Convert]::FromBase64String($catalog.m_BucketDataString)
    $entries=[Convert]::FromBase64String($catalog.m_EntryDataString)
    $extra=[Convert]::FromBase64String($catalog.m_ExtraDataString)
    $count=Read-BapInt32 $buckets 0
    $entryCount=Read-BapInt32 $entries 0
    if ($count -le 0 -or $count -gt 200000 -or $entryCount -le 0 -or $entryCount -gt 200000 -or $entries.Length -ne 4+28*$entryCount) { throw 'Invalid catalog table size.' }
    $bucketRows=New-Object 'System.Collections.Generic.List[object]'
    $keyNames=New-Object 'System.Collections.Generic.List[string]'
    $position=4
    $runtimeBuckets=New-Object 'System.Collections.Generic.List[int]'
    for ($index=0;$index -lt $count;$index++) {
        $offset=Read-BapInt32 $buckets $position
        $length=Read-BapInt32 $buckets ($position+4)
        $position+=8
        if ($length -lt 0 -or $length -gt $entryCount -or $position -gt $buckets.Length-4*$length) { throw 'Invalid catalog bucket.' }
        $rows=New-Object 'System.Collections.Generic.List[int]'
        for($j=0;$j -lt $length;$j++) {
            $row=Read-BapInt32 $buckets ($position+4*$j)
            if($row -lt 0 -or $row -ge $entryCount){throw 'Invalid catalog entry index.'}
            $rows.Add($row)
        }
        $position+=4*$length
        if ($offset -lt 0 -or $offset -ge $keys.Length) { throw 'Invalid catalog key offset.' }
        $name=''
        if ($keys[$offset] -eq 0 -or $keys[$offset] -eq 1) {
            $keyLength=Read-BapInt32 $keys ($offset+1)
            if ($keyLength -lt 0 -or $keyLength -gt 1048576 -or $offset+5 -gt $keys.Length-$keyLength) { throw 'Invalid catalog key size.' }
            if ($keys[$offset] -eq 0) {$name=[Text.Encoding]::UTF8.GetString($keys,$offset+5,$keyLength)}
            else {$name=[Text.Encoding]::Unicode.GetString($keys,$offset+5,$keyLength)}
        }
        $bucketRows.Add($rows)
        $keyNames.Add($name)
        if ($name -ceq 'AstralParty.Runtime.dll') {$runtimeBuckets.Add($index)}
    }
    if ($position -ne $buckets.Length -or $runtimeBuckets.Count -ne 1) { throw 'Missing or ambiguous managed runtime key.' }
    $runtimeRows=$bucketRows[$runtimeBuckets[0]]
    if($runtimeRows.Count -ne 1){throw 'Ambiguous runtime location.'}
    $runtimeOffset=4+28*$runtimeRows[0]
    $assetId=Read-BapInt32 $entries $runtimeOffset
    if($assetId -lt 0 -or $assetId -ge @($catalog.m_InternalIds).Count){throw 'Invalid runtime asset ID.'}
    $dependency=Read-BapInt32 $entries ($runtimeOffset+8)
    $primary=Read-BapInt32 $entries ($runtimeOffset+20)
    $provider=Read-BapInt32 $entries ($runtimeOffset+4)
    $resourceType=Read-BapInt32 $entries ($runtimeOffset+24)
    if($primary -lt 0 -or $primary -ge $count -or $keyNames[$primary] -cne 'AstralParty.Runtime.dll' -or
       $provider -lt 0 -or $provider -ge @($catalog.m_ProviderIds).Count -or
       $catalog.m_ProviderIds[$provider] -cne 'UnityEngine.ResourceManagement.ResourceProviders.BundledAssetProvider' -or
       $resourceType -lt 0 -or $resourceType -ge @($catalog.m_resourceTypes).Count -or
       $catalog.m_resourceTypes[$resourceType].m_ClassName -cne 'UnityEngine.TextAsset') { throw 'Runtime provider/type changed.' }
    if($dependency -lt 0 -or $dependency -ge $count -or $bucketRows[$dependency].Count -ne 1){throw 'Runtime dependency layout changed.'}
    $bundleOffset=4+28*$bucketRows[$dependency][0]
    $bundleId=Read-BapInt32 $entries $bundleOffset
    $bundleProvider=Read-BapInt32 $entries ($bundleOffset+4)
    if ((Read-BapInt32 $entries ($bundleOffset+8)) -ne -1 -or $bundleId -lt 0 -or $bundleId -ge @($catalog.m_InternalIds).Count -or
        $bundleProvider -lt 0 -or $bundleProvider -ge @($catalog.m_ProviderIds).Count -or
        $catalog.m_ProviderIds[$bundleProvider] -cne 'UnityEngine.ResourceManagement.ResourceProviders.AssetBundleProvider') { throw 'Runtime bundle provider/dependencies changed.' }
    $extraOffset=Read-BapInt32 $entries ($bundleOffset+16)
    if($extraOffset -lt 0 -or $extraOffset -ge $extra.Length -or $extra[$extraOffset] -ne 7){throw 'Runtime bundle options missing.'}
    $cursor=$extraOffset+1
    if($cursor -ge $extra.Length){throw 'Truncated bundle options.'}
    $assemblyLength=[int]$extra[$cursor];$cursor++
    if($cursor -gt $extra.Length-$assemblyLength){throw 'Truncated options assembly.'}
    $assembly=[Text.Encoding]::UTF8.GetString($extra,$cursor,$assemblyLength);$cursor+=$assemblyLength
    if($cursor -ge $extra.Length){throw 'Truncated options type.'}
    $typeLength=[int]$extra[$cursor];$cursor++
    if($cursor -gt $extra.Length-$typeLength){throw 'Truncated options type.'}
    $type=[Text.Encoding]::UTF8.GetString($extra,$cursor,$typeLength);$cursor+=$typeLength
    if($assembly -notmatch '^Unity\.ResourceManager, ' -or $type -cne 'UnityEngine.ResourceManagement.ResourceProviders.AssetBundleRequestOptions'){throw 'Unsupported bundle options type.'}
    $jsonLength=Read-BapInt32 $extra $cursor;$cursor+=4
    if($jsonLength -le 0 -or $jsonLength -gt 65536 -or $jsonLength%2 -ne 0 -or $cursor -gt $extra.Length-$jsonLength){throw 'Invalid bundle options size.'}
    $options=[Text.Encoding]::Unicode.GetString($extra,$cursor,$jsonLength) | ConvertFrom-Json
    if($options.m_Hash -cnotmatch '^[a-f0-9]{32}$' -or $options.m_BundleName -cnotmatch '^[a-f0-9]{32}$'){throw 'Invalid bundle cache identity.'}
    $internal=[string]$catalog.m_InternalIds[$bundleId]
    if($internal -cnotmatch ('[\\/]'+[regex]::Escape($options.m_Hash)+'\.bundle$')){throw 'Runtime URL/hash disagree.'}
    [pscustomobject]@{
        Relative=([string]$options.m_BundleName+'/'+[string]$options.m_Hash+'/__data')
        BundleName=[string]$options.m_BundleName
        BundleHash=[string]$options.m_Hash
        RuntimeKey='AstralParty.Runtime.dll'
        RuntimeAssetId=[string]$catalog.m_InternalIds[$assetId]
    }
}
