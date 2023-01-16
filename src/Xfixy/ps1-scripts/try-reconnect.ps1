if (-Not(Test-Connection yahoo.com -Quiet -Count 1)){
    netsh wlan connect name=HOME-8BA8
}
