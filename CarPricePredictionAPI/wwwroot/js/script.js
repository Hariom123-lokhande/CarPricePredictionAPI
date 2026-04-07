// Global fetch wrapper with JWT and 401 handling
async function authorizedFetch(url, options = {}) {
    const token = localStorage.getItem('jwt_token');
    
    // Add Authorization header if token exists
    if (token) {
        options.headers = {
            ...options.headers,
            'Authorization': `Bearer ${token}`
        };
    }

    try {
        const response = await fetch(url, options);
        
        if (response.status === 401) {
            console.warn("Session expired or unauthorized. Redirecting to login...");
            localStorage.removeItem('jwt_token');
            localStorage.removeItem('user_email');
            window.location.href = '/Account/Login';
            return null;
        }
        
        return response;
    } catch (err) {
        console.error("Network Fetch Error:", err);
        throw err;
    }
}

// Auth State Check for Dashboard
document.addEventListener('DOMContentLoaded', () => {
    const path = window.location.pathname.toLowerCase();
    const token = localStorage.getItem('jwt_token');

    // If on Dashboard but no token, redirect to Login
    if (path === '/' || path === '/home/index' || path === '/home') {
        if (!token) {
            window.location.href = '/Account/Login';
            return;
        }
    }

    // Initialize Auth Forms
    initAuthForms();
});

function initAuthForms() {
    //debugger;
    const loginForm = document.getElementById('loginForm');
    const signupForm = document.getElementById('signupForm');

    if (loginForm) {
        loginForm.onsubmit = async (e) => {
            e.preventDefault();
            const btn = loginForm.querySelector('button[type="submit"]');
            const originalText = btn.innerHTML;
            
            btn.disabled = true;
            btn.innerHTML = '<i class="fa-solid fa-spinner fa-spin"></i> Authenticating...';

            const payload = {
                email: loginForm.Email.value,
                password: loginForm.Password.value,
                rememberMe: loginForm.RememberMe?.checked || false
            };

            try {
                const response = await fetch('/Account/Login', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(payload)
                });

                if (response.ok) {
                    const data = await response.json();
                    localStorage.setItem('jwt_token', data.token);
                    localStorage.setItem('user_email', data.email);
                    window.location.href = '/';
                } else {
                    const err = await response.json();
                    const errDiv = document.getElementById('loginError');
                    if (errDiv) errDiv.innerText = err.message || "Invalid credentials.";
                }
            } catch (err) {
                const errDiv = document.getElementById('loginError');
                if (errDiv) errDiv.innerText = "Login failed. Please check your connection.";
            } finally {
                btn.disabled = false;
                btn.innerHTML = originalText;
            }
        };
    }

    if (signupForm) {
        //debugger;
        signupForm.onsubmit = async (e) => {
            e.preventDefault();
            const btn = signupForm.querySelector('button[type="submit"]');
            
            const password = signupForm.Password.value;
            const confirm = signupForm.ConfirmPassword.value;

            const errDiv = document.getElementById('signupError');
            if (errDiv) errDiv.innerText = '';

            if (password !== confirm) {
                if (errDiv) errDiv.innerText = "Passwords do not match.";
                return;
            }

            btn.disabled = true;
            btn.innerHTML = '<i class="fa-solid fa-spinner fa-spin"></i> Creating Account...';

            const payload = {
                email: signupForm.Email.value,
                password: password,
                confirmPassword: confirm
            };

            try {
                const response = await fetch('/Account/Signup', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(payload)
                });

                if (response.ok) {
                    const data = await response.json();
                    localStorage.setItem('jwt_token', data.token);
                    localStorage.setItem('user_email', data.email);
                    // No alert needed, redirecting to dashboard
                    window.location.href = '/';
                } else {
                    const err = await response.json();
                    if (errDiv) {
                        errDiv.innerText = err.errors ? err.errors.join("\n") : (err.message || "Signup failed.");
                    }
                }
            } catch (err) {
                if (errDiv) errDiv.innerText = "Signup failed. Check server connection.";
            } finally {
                btn.disabled = false;
                btn.innerHTML = '<span>Create Your Account</span> <i class="fa-solid fa-user-plus"></i>';
            }
        };
    }
}

// Dashboard Functions


async function predictPrice() {
    //debugger;
    const btn = document.getElementById('predictBtn');
    const resultDiv = document.getElementById('result');
    const priceDisplay = document.getElementById('predictedPrice');

    const brand = document.getElementById('brand').value;
    const year = parseInt(document.getElementById('year').value);
    const mileage = parseInt(document.getElementById('mileage').value);
    const fuel = document.getElementById('fuel').value;
    const transmission = document.getElementById('transmission').value;

    let isValid = true;

    if (!brand) { document.getElementById('brandError').style.display = 'block'; document.getElementById('brandError').innerText = "Please select a brand"; isValid = false; } else { document.getElementById('brandError').style.display = 'none'; }
    
    const currentYear = new Date().getFullYear();
    if (isNaN(year) || year < 2000 || year > currentYear) { document.getElementById('yearError').style.display = 'block'; isValid = false; } else { document.getElementById('yearError').style.display = 'none'; }
    
    if (isNaN(mileage) || mileage < 0 || mileage > 200000) { document.getElementById('mileageError').style.display = 'block'; isValid = false; } else { document.getElementById('mileageError').style.display = 'none'; }
    
    if (!fuel) { document.getElementById('fuelError').style.display = 'block'; document.getElementById('fuelError').innerText = "Please select fuel type"; isValid = false; } else { document.getElementById('fuelError').style.display = 'none'; }

    if (!isValid) return;

    btn.disabled = true;
    btn.innerHTML = '<span>Scanning Matrix...</span> <i class="fa-solid fa-spinner fa-spin"></i>';

    try {
        const response = await authorizedFetch('/api/carprice/predict', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ brand, year, mileage, fuel, transmission, price: 0 })
        });

        if (response && response.ok) {
            const data = await response.json();
            priceDisplay.innerText = data.priceFormatted;
            
            const dealStatusSpan = document.getElementById('dealStatus');
            if (dealStatusSpan) dealStatusSpan.innerText = data.dealStatus || 'Fair Deal 👍';
            
            resultDiv.style.display = 'block';
            resultDiv.scrollIntoView({ behavior: 'smooth' });
        } else if (response) {
            const err = await response.json();
            alert(err.error || "Prediction engine is currently offline.");
        }
    } catch (err) {
        alert("Failed to communicate with the inference engine.");
    } finally {
        btn.disabled = false;
        btn.innerHTML = '<span>Calculate Valuation</span> <i class="fa-solid fa-wand-magic-sparkles"></i>';
    }
}

async function uploadData() {
    //debugger;
    const fileInput = document.getElementById('fileInput');
    const status = document.getElementById('uploadStatus');
    const file = fileInput.files[0];

    if (!file) return;

    const formData = new FormData();
    formData.append('file', file);

    status.innerHTML = '<i class="fa-solid fa-spinner fa-spin"></i> Injecting dataset...';
    
    try {
        const response = await authorizedFetch('/api/carprice/upload', {
            method: 'POST',
            body: formData
        });

        if (response && response.ok) {
            status.innerHTML = '<span style="color:#10b981"><i class="fa-solid fa-check"></i> Data synchronization complete.</span>';
        } else if (response) {
            status.innerHTML = '<span style="color:#ef4444"><i class="fa-solid fa-xmark"></i> Data rejection. Check CSV format.</span>';
        }
    } catch (err) {
        status.innerHTML = '<span style="color:#ef4444">Connection failure during data transfer.</span>';
    }
}

async function trainModel() {
    //debugger;
    const btn = document.getElementById('trainBtn');
    const stats = document.getElementById('trainStats');
    const algo = document.getElementById('algorithm').value;

    btn.disabled = true;
    btn.innerHTML = '<span>Synthesizing Intelligence...</span> <i class="fa-solid fa-bolt-lightning fa-beat"></i>';
    stats.style.display = 'none';

    try {
        const response = await authorizedFetch(`/api/carprice/train?algorithm=${algo}`, {
            method: 'POST'
        });

        if (response && response.ok) {
            const data = await response.json();
            
            // Hide everything first
            stats.style.display = 'none';
            document.getElementById('comparisonStats').style.display = 'none';
            document.getElementById('modelInsight').style.display = 'none';

            if (algo === 'COMPARE') {
                document.getElementById('sdcaR2').innerText = (data.sdca.rSquared * 100).toFixed(1) + '%';
                document.getElementById('sdcaTime').innerText = data.sdca.time;
                document.getElementById('ftR2').innerText = (data.fastTree.rSquared * 100).toFixed(1) + '%';
                document.getElementById('ftTime').innerText = data.fastTree.time;
                document.getElementById('comparisonStats').style.display = 'grid';
                
                document.getElementById('insightTitle').innerText = 'Why is SDCA faster? ⭐';
                document.getElementById('insightBody').innerText = "SDCA provides faster training and prediction due to its linear approach and low computational cost, making it ideal for real-time scenarios, while FastTree is slower as it constructs multiple decision trees but can model more complex, non-linear relationships.";
                document.getElementById('modelInsight').style.display = 'flex';
            } else {
                document.getElementById('r2Score').innerText = (data.rSquared * 100).toFixed(1) + '%';
                document.getElementById('maeScore').innerText = '₹' + (data.rmse || 0).toLocaleString();
                document.getElementById('r2Bar').style.width = (data.rSquared * 100) + '%';
                stats.style.display = 'grid';

                document.getElementById('insightTitle').innerText = 'Algorithm Insight';
                document.getElementById('insightBody').innerText = data.algorithm === 'SDCA' 
                    ? "SDCA is optimized for speed and linear efficiency."
                    : "FastTree captures complex non-linear nuances for maximum precision.";
                document.getElementById('modelInsight').style.display = 'flex';
            }
            
            btn.innerHTML = '<span>Optimization Success</span> <i class="fa-solid fa-check-double"></i>';
        } else if (response) {
            alert("Training failed. Please ensure a valid dataset is uploaded.");
            btn.disabled = false;
            btn.innerHTML = '<span>Retry Training</span> <i class="fa-solid fa-bolt-lightning"></i>';
        }
    } catch (err) {
        alert("Critical failure during model synthesis.");
        btn.disabled = false;
    }
}

function handleLogout() {
    localStorage.removeItem('jwt_token');
    localStorage.removeItem('user_email');
    window.location.href = '/Account/Login';
}
