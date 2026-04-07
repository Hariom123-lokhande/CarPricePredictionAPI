async function login() {
    debugger;
    const user = document.getElementById('username').value;
    const pass = document.getElementById('password').value;
    const res = await fetch('/api/auth/login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ username: user, password: pass })
    });
    if(res.ok) {
        const data = await res.json();
        localStorage.setItem('jwt_token', data.token);
        window.location.href = '/Dashboard';
    } else {
        document.getElementById('error').innerText = 'Login Failed';
    }
}

async function signup() {
    const user = document.getElementById('username').value;
    const pass = document.getElementById('password').value;
    const res = await fetch('/api/auth/signup', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ username: user, password: pass })
    });
    if(res.ok) {
        const data = await res.json();
        localStorage.setItem('jwt_token', data.token);
        window.location.href = '/Energy/Dashboard';
    } else {
        document.getElementById('error').innerText = 'Signup Failed. Please ensure password requires normal formatting (e.g., minimum length).';
    }
}
