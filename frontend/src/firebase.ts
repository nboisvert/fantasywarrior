import { initializeApp } from "firebase/app";

// Public web app config for project fantasywarriordb
// (safe to commit — access is enforced by Firestore security rules).
const firebaseConfig = {
  apiKey: "AIzaSyBVjSJRmN-Bwy75A0DG9vRu3fL5RFXGJPQ",
  authDomain: "fantasywarriordb.firebaseapp.com",
  projectId: "fantasywarriordb",
  storageBucket: "fantasywarriordb.firebasestorage.app",
  messagingSenderId: "197228637471",
  appId: "1:197228637471:web:8b4286565bf06930c5542a",
};

export const firebaseApp = initializeApp(firebaseConfig);
