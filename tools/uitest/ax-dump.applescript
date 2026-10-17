on av(el, key)
	try
		tell application "System Events"
			tell el
				if key = "role" then
					set v to value of attribute "AXRole"
				else if key = "id" then
					set v to value of attribute "AXIdentifier"
				else if key = "dsc" then
					set v to value of attribute "AXDescription"
				else if key = "ttl" then
					set v to value of attribute "AXTitle"
				else if key = "val" then
					set v to value of attribute "AXValue"
				else
					set v to ""
				end if
			end tell
		end tell
		if v is missing value then
			return ""
		end if
		return v as string
	on error
		return ""
	end try
end av

set out to ""
tell application "System Events"
	tell process "TravelAgency.App"
		repeat with w in windows
			set out to out & "=== VENTANA: " & (name of w) & " ===" & linefeed
			repeat with el in (entire contents of w)
				set out to out & my av(el, "role") & " | id=[" & my av(el, "id") & "] dsc=[" & my av(el, "dsc") & "] val=[" & my av(el, "val") & "]" & linefeed
			end repeat
		end repeat
	end tell
end tell
return out
