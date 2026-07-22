import { initializeApp } from "firebase/app";

// Public web app config (safe to commit — access is enforced by security rules).
const firebaseConfig = {
  apiKey: "AIzaSyDcMhSpvKRrgIRNB_1I-iCN3OLFWPKI8K0",
  authDomain: "requinopen.firebaseapp.com",
  databaseURL: "https://requinopen-default-rtdb.firebaseio.com",
  projectId: "requinopen",
  storageBucket: "requinopen.firebasestorage.app",
  messagingSenderId: "151123063839",
  appId: "1:151123063839:web:629552bc3a50a0a9b7512a",
};

export const firebaseApp = initializeApp(firebaseConfig);
