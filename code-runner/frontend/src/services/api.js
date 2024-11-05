import axios from 'axios';

const NUMBER_A = 5113;
const NUMBER_B = 9973;
const NUMBER_C = 314159;

console.log(import.meta.env.API_BASE_URL)
const apiClient = axios.create({
  baseURL: import.meta.env.API_BASE_URL,
  headers: {
    'Content-Type': 'application/json',
  },
});

export const analyzeWeibo = async (uid) => {
  try {
    const t = new Date().getTime()
    const sign = (t % NUMBER_A + Number(uid) % NUMBER_B) % NUMBER_C
    const payload = {
        inputs: {
          Uid: uid
        },
        timestamp: t.toString(),
        signature: sign.toString(),
        version: "^1.2"
    }

    const response = await apiClient.post(`/run`, payload);
    return response.data;
  } catch (error) {
    console.error('Error analyzing Weibo:', error);
    throw error;
  }
};