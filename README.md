
![TP-Link Client](http://i.imgur.com/MQBZUJF.gif)

# Instructions
**TPLinkClient.ini** with settings must be located 
next to TPLinkClient.exe (working directory).

Structure of **TPLinkClient.ini** file:

<pre>
IP=192.168.1.1
Port=23
Username=admin
Password=admin
WANInterface=pppoe_0_35_3_d
AutoUpdate=true
</pre>

**IP** - TP-Link router IP address <br>
**Port** - Default telnet port <br>
**Username** - TP-Link router username (default admin) <br>
**Password** - TP-Link router password (default admin) <br>
**WANInterface** - WAN interface name (displayed in status page) <br>
**AutoUpdate** - Enable or disable auto refreshing feature <br>

Application is using telnet protocol to connect to the router and get needed informations, which are:

+ **(A)DSL connection status**
+ **External IP address** (no need to use 'whatsmyip' sites anymore)
+ **(A)DSL last synchronization time** (uptime)

Tested with router TP-Link TD-W8970.
