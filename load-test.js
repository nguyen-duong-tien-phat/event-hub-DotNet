import http from 'k6/http';
import { check } from 'k6';

const BASE_URL = 'https://localhost:8080'; // update to your actual port
const TICKET_ID = 'PASTE_YOUR_TICKET_ID_HERE';
const TOKEN = 'PASTE_YOUR_JWT_TOKEN_HERE';

export const options = {
    scenarios: {
        concurrent_bookings: {
            executor: 'shared-iterations',
            vus: 30,           // 30 virtual users
            iterations: 30,     // exactly 30 total requests
            maxDuration: '10s',
        },
    },
};

export default function () {
    const payload = JSON.stringify({
        ticketId: TICKET_ID,
        quantity: 1,
    });

    const params = {
        headers: {
            'Content-Type': 'application/json',
            'Authorization': `Bearer ${TOKEN}`,
        },
    };

    const res = http.post(`${BASE_URL}/api/bookings`, payload, params);

    check(res, {
        'booking succeeded (201)': (r) => r.status === 201,
        'booking rejected (409 - sold out)': (r) => r.status === 409,
    });

    console.log(`Status: ${res.status}`);
}