import axios from 'axios';
import { getEnv } from '../utils/getEnv';
const baseUrl = getEnv('VITE_BASE_API_URL', 'http://localhost:81/networkhost/api') as string;

const axiosInstance = axios.create({
  baseURL: baseUrl,
  withCredentials: true,
  headers: {
    'Content-Type': 'application/json',
  },
  timeout: 30000,
});

export default axiosInstance;