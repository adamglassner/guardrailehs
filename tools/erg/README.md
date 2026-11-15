# ERG data

`src/Guardrail.Desktop/Assets/erg2024.json.gz` is the 2024 Emergency Response Guidebook
(U.S. DOT/PHMSA, Transport Canada, Mexico SCT) turned into data for the Safety References page:

- the yellow-page index (ID number → guide → material name),
- all orange guides 111–174,
- Table 1 initial isolation and protective action distances,
- Table 2 water-reactive materials, and
- the General First Aid page.

To rebuild it from the official PDF (poppler-utils needed):

```
pdftotext -layout ERG2024.pdf erg-layout.txt
python3 tools/erg/parse.py erg-layout.txt erg.json
gzip -9 -c erg.json > src/Guardrail.Desktop/Assets/erg2024.json.gz
```

The parser expects the 2024 page layout; a new edition will need its page ranges checked.
