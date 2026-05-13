// Firebase Web SDK
import { initializeApp } from "https://www.gstatic.com/firebasejs/11.0.1/firebase-app.js";
import { getAuth, 
         createUserWithEmailAndPassword,
         signInWithEmailAndPassword,
         signOut,
         setPersistence,
         browserSessionPersistence } from "https://www.gstatic.com/firebasejs/11.0.1/firebase-auth.js";

import { getFirestore, doc, setDoc, getDoc } from "https://www.gstatic.com/firebasejs/11.0.1/firebase-firestore.js";

const firebaseConfig = {
    apiKey: "AIzaSyBly78fwVcU4QxPaXMEOmhWwOQolJC3Y2Q",
    authDomain: "meetagain-50f2b.firebaseapp.com",
    projectId: "meetagain-50f2b"
};

// Init
const app = initializeApp(firebaseConfig);
const auth = getAuth(app);
const db = getFirestore(app);

// Set auth persistence to SESSION (only while browser tab is open)
setPersistence(auth, browserSessionPersistence);


// === REGISTER ===
export async function registerUser(name, email, password) {
    const result = await createUserWithEmailAndPassword(auth, email, password);
    const user = result.user;

    await setDoc(doc(db, "users", user.uid), {
        uid: user.uid,
        name,
        email,
        createdAt: new Date().toISOString()
    });

    return user.uid;
}

// === LOGIN ===
export async function loginUser(email, password) {
    const result = await signInWithEmailAndPassword(auth, email, password);
    return result.user.uid;
}

// === LOAD USER PROFILE ===
export async function loadUser(uid) {
    const snap = await getDoc(doc(db, "users", uid));
    return snap.exists() ? snap.data() : null;
}

// === LOGOUT ===
// === LOGOUT ===
export async function logout() {
    await signOut(auth);
}

// Make logout globally accessible for Blazor
window.logout = logout;
