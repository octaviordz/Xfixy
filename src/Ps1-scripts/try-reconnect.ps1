if (-Not(Test-Connection yahoo.com -Quiet -Count 1)){
    netsh wlan connect name="XT1687 4058"
}
# XT1687 4058
# HOME-8BA8