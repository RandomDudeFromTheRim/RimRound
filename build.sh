#!/bin/bash
# RimRound build script for Linux (mono)

CSC=/usr/bin/csc
OUTPUT="/home/moffer/Desktop/kinky/RimRound/1.6/Assemblies/RimRound.dll"
SRCDIR="/home/moffer/Desktop/kinky/RimRound/Source/RimRound"
RWDIR="/home/moffer/Desktop/kinky/RimWorld/RimWorldWin64_Data/Managed"

REFS=""
REFS+=" -reference:${RWDIR}/Assembly-CSharp.dll"
REFS+=" -reference:${RWDIR}/Assembly-CSharp-firstpass.dll"
REFS+=" -reference:${RWDIR}/UnityEngine.dll"
REFS+=" -reference:${RWDIR}/UnityEngine.CoreModule.dll"
REFS+=" -reference:${RWDIR}/UnityEngine.IMGUIModule.dll"
REFS+=" -reference:${RWDIR}/UnityEngine.TextRenderingModule.dll"
REFS+=" -reference:${RWDIR}/UnityEngine.InputLegacyModule.dll"
REFS+=" -reference:/home/moffer/Desktop/kinky/HarmonyMod/Current/Assemblies/0Harmony.dll"
REFS+=" -reference:/home/moffer/Desktop/kinky/AlienRaces-1.6.0/1.6/Assemblies/AlienRace.dll"
REFS+=" -reference:/usr/lib/mono/4.5/System.dll"
REFS+=" -reference:/usr/lib/mono/4.5/System.Core.dll"
REFS+=" -reference:/usr/lib/mono/4.5/System.Xml.dll"
REFS+=" -reference:/usr/lib/mono/4.5/System.Xml.Linq.dll"
REFS+=" -reference:/usr/lib/mono/4.5/System.Data.dll"
REFS+=" -reference:/usr/lib/mono/4.5/System.Net.Http.dll"
REFS+=" -reference:/usr/lib/mono/4.5/Facades/netstandard.dll"

SOURCES=$(find "${SRCDIR}/RimRound" "${SRCDIR}/FeedingTube" -name "*.cs" \
  -not -path "*/Properties/*" -not -path "*/Notes*" -not -path "*/obj/*" \
  -not -name "AssemblyInfo.cs" 2>/dev/null | sort)

echo "Building RimRound from ${SRCDIR}..."
echo "Found $(echo "${SOURCES}" | wc -l) source files."
echo "Output: ${OUTPUT}"
echo ""

mkdir -p "$(dirname "${OUTPUT}")"

${CSC} -target:library -out:"${OUTPUT}" -noconfig \
  -define:DEBUG -define:TRACE \
  -optimize+ \
  ${REFS} \
  ${SOURCES} 2>&1

if [ $? -eq 0 ]; then
  echo ""
  echo "BUILD SUCCESSFUL - ${OUTPUT}"
  ls -lh "${OUTPUT}"
else
  echo ""
  echo "BUILD FAILED"
  exit 1
fi
