
![TP-Link Client](http://i.imgur.com/MQBZUJF.gif)

# Instructions
**TPLinkClient.ini** with settings must be located 
next to TPLinkClient.exe (working directory).

Structure of **TPLinkClient.ini** file:

<pre>
ip=192.168.1.1
port=23
username=admin
password=admin
waninterface=pppoe_0_35_3_d
</pre>

**ip** - TP-Link router IP address <br>
**port** - default telnet port <br>
**username** - TP-Link router username (default admin) <br>
**password** - TP-Link router password (default admin) <br>
**waninterface** - WAN interface name (displayed in status page) <br>

Application is using telnet protocol to connect to the router and get needed information, which are:

+ **(A)DSL connection status**
+ **External IP address** (no need to use 'whatsmyip' sites anymore)
+ **(A)DSL last synchronization time** (uptime)

Tested with router TP-Link TD-W8970.
