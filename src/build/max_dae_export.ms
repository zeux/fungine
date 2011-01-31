function getExportPlugin name =
(
	for plug in exporterPlugin.classes do
	(
		if plug as string == name then return (plug)
	)
	
	throw ("Plugin " + name + " not found")
)

function exportOpenCollada source target =
(
    plug = getExportPlugin "OpenCOLLADAExporter"

    options = openFile "$plugcfg/../OpenCOLLADA.ini" mode:"wt"
    print "[OpenCOLLADA]\ntangents=1\nsampleAnim=0\nbakeMatrices=1\ncopyImages=0\nexportUserdefinedProperties=1" to:options
    close options

    if not loadMaxFile source quiet:true then throw ("Error loading file " + source)
    if not exportFile target #noPrompt using:plug then throw ("Error exporting to file " + target)
)

function export source target =
(
    exportOpenCollada source target
)
