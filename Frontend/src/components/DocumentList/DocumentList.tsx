import React from "react";
import { List, ListItem, ListItemButton, ListItemText } from "@mui/material";
import { DocumentListProps } from "./DocumentListProps";

const DocumentList: React.FC<DocumentListProps> = ({
  documents,
  selectedDocument,
  onSelectDocument,
}) => {
  return (
    <List
      sx={{
        width: "100%",
        maxWidth: 360,
        bgcolor: "background.paper",
      }}
    >
      {documents?.map((document) => (
        <ListItem disablePadding key={document.id}>
          <ListItemButton
            selected={selectedDocument?.id === document.id}
            onClick={() => onSelectDocument(document)}
          >
            <ListItemText primary={document.name} />
          </ListItemButton>
        </ListItem>
      ))}
    </List>
  );
};

export default DocumentList;
